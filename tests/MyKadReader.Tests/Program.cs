using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using MyKadReader;
using MyKadReader.Core;

internal static class Program
{
    private static int passed;
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--probe-reader"))
            {
                var readers = new PcscReader().Snapshot();
                Console.WriteLine($"PC/SC readers: {readers.Count}");
                foreach (var reader in readers) Console.WriteLine($"{reader.Name}: card present = {reader.Present}");
                return 0;
            }
            if (args.Contains("--verify-card"))
            {
                var service = new PcscReader();
                var state = service.Snapshot().FirstOrDefault(r => r.Present);
                if (state is null) { Console.WriteLine("No inserted card detected. No identity data read or saved."); return 2; }
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var identity = service.Read(state.Name, cancellation.Token);
                bool photoValid = false;
                if (identity.Photo is not null) { IdentityView.DecodePhoto(identity.Photo); photoValid = true; }
                Console.WriteLine($"Read succeeded. Number present: {identity.Number.Length == 12}; Name: {identity.Name is not null}; DOB: {identity.BirthDate is not null}; Gender: {identity.Gender is not null}; Address: {identity.Address is not null}; Decodable photo: {photoValid}. No identity values printed or saved.");
                return photoValid ? 0 : 3;
            }
            Test("Personal fields and BCD date", () =>
            {
                var value = MyKadDecoder.Personal(Personal());
                Equal("000101000000", value.Number); Equal("FICTIONAL PERSON", value.Name);
                Equal("2000-01-01", value.BirthDate); Equal("Male", value.Gender);
            });
            Test("Truncated and unsupported text rejected", () =>
            {
                Throws<InvalidDataException>(() => MyKadDecoder.Personal(new byte[4]));
                var data = Personal(); data[0] = 0xFF; Throws<InvalidDataException>(() => MyKadDecoder.Personal(data));
                data = Personal(); data[0x10E] = (byte)'X'; Throws<InvalidDataException>(() => MyKadDecoder.Personal(data));
            });
            Test("Invalid date and gender remain unavailable", () =>
            {
                var data = Personal(); data[0x126] = 0x99; data[0x11B] = (byte)'?';
                var value = MyKadDecoder.Personal(data); Equal<string?>(null, value.BirthDate); Equal<string?>(null, value.Gender);
            });
            Test("Address decoding", () =>
            {
                var data = new byte[0x94]; Encoding.ASCII.GetBytes("1 DEMO STREET").CopyTo(data, 0);
                data[0x5A] = 0x12; data[0x5B] = 0x34; data[0x5C] = 0x50;
                True(MyKadDecoder.Address(data)!.Contains("12345"));
                Throws<InvalidDataException>(() => MyKadDecoder.Address([]));
            });
            Test("JPEG framing rejects incomplete data", () =>
            {
                Throws<InvalidDataException>(() => MyKadDecoder.Photo([0, 1, 2, 3]));
                Throws<InvalidDataException>(() => MyKadDecoder.Photo([0xFF, 0xD8, 0, 0]));
                Equal(5, MyKadDecoder.Photo([0xFF, 0xD8, 0, 0xFF, 0xD9, 0]).Length);
            });
            Test("Protocol chunks and optional fields", () =>
            {
                using var channel = new FakeChannel();
                var identity = new CardProtocol(channel, CancellationToken.None).Read();
                Equal("000101000000", identity.Number); True(identity.Photo is null); True(channel.ReadCount == 4);
            });
            Test("Protocol cancellation and failures", () =>
            {
                using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                using var channel = new FakeChannel();
                Throws<OperationCanceledException>(() => new CardProtocol(channel, cancelled.Token).Read()); Equal(0, channel.ReadCount);
                using var rejected = new FakeChannel { RejectSelect = true };
                Throws<CardCommandException>(() => new CardProtocol(rejected, CancellationToken.None).Read());
                using var truncated = new FakeChannel { Truncate = true };
                Throws<InvalidDataException>(() => new CardProtocol(truncated, CancellationToken.None).Read());
            });
            string folder = Path.Combine(Path.GetTempPath(), "MyKad tests 空格 " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try { DatabaseTests(folder); }
            finally { Directory.Delete(folder, true); }
            UiTests();
            Console.WriteLine($"PASS: {passed} tests. Hardware reads and clean-machine portability remain manual acceptance checks.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void DatabaseTests(string folder)
    {
        string path = Path.Combine(folder, "Data", "mykad.db");
        var repository = new IdentityRepository(path);
        var identity = new Identity("000101-00-0000", "FICTIONAL PERSON", "2000-01-01", "Male", "Demo address", [1, 2, 3]);
        Test("Read-only access never creates database", () =>
        {
            Equal(0L, repository.Search("", 0).Total); True(repository.Get("000101000000") is null);
            repository.Delete("000101000000"); True(!Directory.Exists(Path.GetDirectoryName(path)));
        });
        Test("Save, reopen, photo round-trip and normalized identity", () =>
        {
            repository.Save(identity);
            var stored = new IdentityRepository(path).Get("000101000000")!;
            Equal(identity.Name, stored.Identity.Name); True(stored.Identity.Photo!.SequenceEqual(identity.Photo!));
            True(DateTimeOffset.TryParse(stored.CreatedUtc, out _));
        });
        Test("Duplicate update replaces missing fields and preserves creation", () =>
        {
            var before = repository.Get("000101000000")!;
            repository.Save(identity with { Name = "UPDATED DEMO", Photo = null, Address = null });
            var after = repository.Get("000101000000")!;
            Equal(before.CreatedUtc, after.CreatedUtc); True(after.UpdatedUtc != before.UpdatedUtc);
            True(after.Identity.Photo is null && after.Identity.Address is null); Equal(1L, repository.Search("", 0).Total);
        });
        Test("Search and pagination use literal parameters", () =>
        {
            for (int i = 1; i <= 30; i++) repository.Save(identity with { Number = $"000101{i:000000}" });
            Equal(25, repository.Search("", 0).Items.Count); Equal(6, repository.Search("", 1).Items.Count);
            Equal(30L, repository.Search("fictional", 0).Total);
            Equal(1L, repository.Search("000101-00-0000", 0).Total);
            Equal(0L, repository.Search("' OR 1=1 --", 0).Total); Equal(0L, repository.Search("%", 0).Total);
        });
        Test("Locked write fails without changing existing record", () =>
        {
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                connection.Open(); using var transaction = connection.BeginTransaction();
                using var command = connection.CreateCommand(); command.Transaction = transaction;
                command.CommandText = "UPDATE identities SET name=name"; command.ExecuteNonQuery();
                Throws<SqliteException>(() => repository.Save(identity with { Name = "SHOULD NOT SAVE" }));
                transaction.Rollback();
            }
            Equal("UPDATED DEMO", repository.Get("000101000000")!.Identity.Name);
        });
        Test("Copy to another folder retains records and photos", () =>
        {
            string moved = Path.Combine(folder, "moved.db"); File.Copy(path, moved);
            var copy = new IdentityRepository(moved);
            Equal(31L, copy.Search("", 0).Total); True(copy.Get("000101000001")!.Identity.Photo!.SequenceEqual(identity.Photo!));
        });
        Test("Read-only database remains viewable", () =>
        {
            File.SetAttributes(path, FileAttributes.ReadOnly);
            try { Equal(31L, repository.Search("", 0).Total); Throws<SqliteException>(() => repository.Save(identity)); }
            finally { File.SetAttributes(path, FileAttributes.Normal); }
        });
        Test("Delete removes identity and embedded photo", () =>
        {
            repository.Delete("000101000001"); True(repository.Get("000101000001") is null); Equal(30L, repository.Search("", 0).Total);
        });
        Test("Corrupt and newer-schema databases are preserved", () =>
        {
            string bad = Path.Combine(folder, "bad.db"); byte[] content = Encoding.UTF8.GetBytes("not a database"); File.WriteAllBytes(bad, content);
            Throws<SqliteException>(() => new IdentityRepository(bad).Save(identity)); True(File.ReadAllBytes(bad).SequenceEqual(content));
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "PRAGMA user_version=99"; command.ExecuteNonQuery(); }
            Throws<UserVisibleException>(() => repository.Save(identity));
        });
    }

    private static void UiTests()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var application = new App(); application.InitializeComponent();
        Test("WPF identity view decodes photo and renders", () =>
        {
            var pixels = Enumerable.Repeat((byte)120, 80 * 100 * 3).ToArray();
            var image = BitmapSource.Create(80, 100, 96, 96, PixelFormats.Rgb24, null, pixels, 80 * 3);
            var jpeg = new JpegBitmapEncoder(); jpeg.Frames.Add(BitmapFrame.Create(image));
            using var stream = new MemoryStream(); jpeg.Save(stream);
            var view = new IdentityView(); view.ShowIdentity(new("000101000000", "FICTIONAL DEMO PERSON", "2000-01-01", "Male", "1 Demo Street\nExample Town", stream.ToArray()));
            view.Measure(new Size(900, 400)); view.Arrange(new Rect(0, 0, 900, 400)); view.UpdateLayout();
            var bitmap = new RenderTargetBitmap(900, 400, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
            string output = Path.Combine(AppContext.BaseDirectory, "identity-preview.png");
            using var file = File.Create(output); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); png.Save(file);
            True(IdentityView.DecodePhoto(stream.ToArray()).PixelWidth > 0);
        });
        Test("Reader UI cancellation, explicit save, card removal and late-result rejection", () =>
        {
            string folder = Path.Combine(Path.GetTempPath(), "MyKad UI " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string database = Path.Combine(folder, "Data", "mykad.db");
            var controlled = new ControlledReader();
            var window = new MainWindow(true, controlled, database);
            window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            PumpUntil(() => ((Button)window.FindName("ReadButton")).IsEnabled);
            var read = (Button)window.FindName("ReadButton");
            read.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((Button)window.FindName("CancelButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => read.IsEnabled);
            True(!((Button)window.FindName("SaveButton")).IsEnabled);
            read.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => ((Button)window.FindName("SaveButton")).IsEnabled);
            True(!File.Exists(database));
            ((Button)window.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => ((TextBlock)window.FindName("ReadResult")).Text.StartsWith("Saved to"));
            PumpUntil(() => ((DataGrid)window.FindName("Records")).Items.Count == 1);
            Equal(1L, new IdentityRepository(database).Search("", 0).Total);
            var records = (DataGrid)window.FindName("Records"); records.SelectedIndex = 0;
            PumpUntil(() => ((Button)window.FindName("DeleteButton")).IsEnabled);
            controlled.State = new("TEST READER", false, 0x10000);
            PumpUntil(() => !((Button)window.FindName("SaveButton")).IsEnabled);
            True(((Button)window.FindName("DeleteButton")).IsEnabled); // Saved record is independent of removal.
            controlled.State = new("TEST READER", true, 0x20020);
            PumpUntil(() => read.IsEnabled);
            controlled.Delay = 1000;
            read.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            controlled.State = new("TEST READER", false, 0x30000);
            PumpUntil(() => ((TextBlock)window.FindName("ReadResult")).Text.Contains("state changed"));
            controlled.State = new("TEST READER", true, 0x40020);
            PumpUntil(() => read.IsEnabled);
            True(!((Button)window.FindName("SaveButton")).IsEnabled);
            controlled.Delay = 50;
            read.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => ((Button)window.FindName("SaveButton")).IsEnabled);
            ((Button)window.FindName("ClearButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            True(!((Button)window.FindName("SaveButton")).IsEnabled);
            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(980, 750)); content.Arrange(new Rect(0, 0, 980, 750)); content.UpdateLayout();
            var bitmap = new RenderTargetBitmap(980, 750, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
            using (var output = File.Create(Path.Combine(AppContext.BaseDirectory, "window-preview.png")))
            { var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); png.Save(output); }
            window.Close();
            Directory.Delete(folder, true);
        });
        application.Shutdown();
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var end = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > end) throw new Exception("UI operation timed out.");
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame); Thread.Sleep(10);
        }
    }

    private static byte[] Personal()
    {
        var data = new byte[0x1A5]; Encoding.ASCII.GetBytes("FICTIONAL PERSON").CopyTo(data, 0);
        Encoding.ASCII.GetBytes("000101000000").CopyTo(data, 0x10E); data[0x11B] = (byte)'L';
        new byte[] { 0x20, 0x00, 0x01, 0x01 }.CopyTo(data, 0x124); return data;
    }
    private static void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
    private static void True(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }

    private sealed class FakeChannel : ICardChannel
    {
        public int ReadCount { get; private set; }
        public bool RejectSelect { get; init; }
        public bool Truncate { get; init; }
        private int file, offset;
        public byte[] Transmit(byte[] command)
        {
            if (command[1] == 0xA4) return RejectSelect ? [0x6A, 0x82] : [0x61, 5];
            if (command[1] == 0xC0) return [0, 0, 0, 0, 0, 0x90, 0];
            if (command[0] == 0xC8) return [0x91, 8];
            if (command[0] == 0xCC && command[1] == 0)
            { file = command[5]; offset = command[9] + (command[10] << 8); return [0x94, command[11]]; }
            if (command[1] == 6)
            {
                ReadCount++;
                if (file != 1) return [0x6A, 0x82];
                var data = Personal().Skip(offset - 3).Take(Truncate ? 1 : command[4]).ToArray();
                return [.. data, (byte)(ReadCount == 1 ? 0x94 : 0x90), 0];
            }
            return [0x90, 0];
        }
        public void Dispose() { }
    }

    private sealed class ControlledReader : ICardReader
    {
        public volatile ReaderState State = new("TEST READER", true, 0x20);
        public volatile int Delay = 600;
        public IReadOnlyList<ReaderState> Snapshot() => [State];
        public Identity Read(string reader, CancellationToken cancellation)
        {
            // Intentionally ignores cancellation to exercise the UI's stale-result guard.
            Thread.Sleep(Delay);
            return new("000101000000", "FICTIONAL TEST PERSON", "2000-01-01", "Male", "1 Demo Street", null);
        }
    }
}
