using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using MyKadReader.Core;

namespace MyKadReader;

public partial class MainWindow : Window
{
    private readonly ICardReader reader;
    private readonly IdentityRepository repository;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private IReadOnlyList<ReaderState> states = [];
    private CancellationTokenSource? readCancellation;
    private Identity? current;
    private bool reading, saving, polling, refreshingReaders, writable, closed, loadingRecords, deleting;
    private int generation, recordGeneration, page;
    private string query = "";
    private long total;
    private SavedIdentity? selected;

    public MainWindow(bool simulate, ICardReader? readerOverride = null, string? databasePath = null)
    {
        InitializeComponent();
        reader = readerOverride ?? (simulate ? new SimulatedReader() : new PcscReader());
        repository = new(databasePath ?? Path.Combine(AppContext.BaseDirectory, "Data", simulate ? "simulator.db" : "mykad.db"));
        if (simulate) ModeLabel.Text = "DEVELOPMENT SIMULATOR — fictional data; saves go to Data/simulator.db.";
        CurrentView.ShowIdentity(null); SavedView.ShowIdentity(null);
        Loaded += async (_, _) => { CheckStorage(); await Poll(); await LoadRecords(); timer.Start(); };
        timer.Tick += async (_, _) => await Poll();
        Closing += (_, e) =>
        {
            if (saving || deleting) { e.Cancel = true; return; }
            closed = true; generation++; recordGeneration++; readCancellation?.Cancel(); timer.Stop();
        };
    }

    private void CheckStorage()
    {
        try
        {
            string directory = Path.GetDirectoryName(repository.DatabasePath)!;
            if (!Directory.Exists(directory)) directory = AppContext.BaseDirectory;
            string probe = Path.Combine(directory, ".mykad-write-" + Guid.NewGuid().ToString("N"));
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            if (File.Exists(repository.DatabasePath))
                using (new FileStream(repository.DatabasePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) { }
            writable = true;
            StorageStatus.Text = "Database: " + repository.DatabasePath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            writable = false;
            StorageStatus.Text = "Saving/deletion unavailable. Move the app and Data folder to a writable local folder, then retry. Reading cards remains available.";
        }
        Buttons();
    }

    private void Buttons()
    {
        var state = states.FirstOrDefault(x => x.Name == Readers.SelectedItem as string);
        ReadButton.IsEnabled = !reading && !saving && state?.Present == true;
        CancelButton.IsEnabled = reading && readCancellation?.IsCancellationRequested == false;
        ClearButton.IsEnabled = !saving;
        SaveButton.IsEnabled = !reading && !saving && !deleting && current is not null && writable;
        Readers.IsEnabled = !reading && !saving;
        DeleteButton.IsEnabled = !deleting && !saving && selected is not null && writable;
        PreviousButton.IsEnabled = !loadingRecords && page > 0;
        NextButton.IsEnabled = !loadingRecords && (page + 1L) * 25 < total;
    }

    private void InvalidateRead()
    {
        generation++; readCancellation?.Cancel(); current = null;
        CurrentView.ShowIdentity(null); Buttons();
    }

    private async Task Poll()
    {
        if (polling || closed) return;
        polling = true;
        try
        {
            var snapshot = await Task.Run(reader.Snapshot);
            if (closed) return;
            string? name = Readers.SelectedItem as string;
            var before = states.FirstOrDefault(x => x.Name == name);
            var after = snapshot.FirstOrDefault(x => x.Name == name);
            if (before is not null && (after is null || before.Revision != after.Revision))
            {
                InvalidateRead(); ReadResult.Text = "Card state changed. Read the inserted card again.";
            }
            states = snapshot;
            var names = snapshot.Select(x => x.Name).ToArray();
            if (!Readers.Items.Cast<string>().SequenceEqual(names))
            {
                refreshingReaders = true;
                Readers.ItemsSource = names;
                Readers.SelectedItem = names.Contains(name) ? name : names.FirstOrDefault();
                refreshingReaders = false;
            }
            var state = states.FirstOrDefault(x => x.Name == Readers.SelectedItem as string);
            ReaderStatus.Text = names.Length == 0 ? "No reader detected. Connect the Alcorlink reader and check its Windows driver."
                : state?.Present == true ? "Card detected • Ready to read" : "Reader connected • Insert a MyKad";
        }
        catch (Exception ex)
        {
            if (current is not null || reading) InvalidateRead();
            states = []; ReaderStatus.Text = Friendly(ex);
        }
        finally { polling = false; Buttons(); }
    }

    private void ReaderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (refreshingReaders || !IsLoaded) return;
        InvalidateRead(); ReadResult.Text = ""; Buttons();
    }

    private async void ReadClick(object sender, RoutedEventArgs e)
    {
        if (reading || saving || Readers.SelectedItem is not string name) return;
        InvalidateRead();
        int request = generation;
        var initial = states.FirstOrDefault(x => x.Name == name);
        readCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellation = readCancellation;
        reading = true; ReadResult.Text = "Reading card… Keep it inserted."; Buttons();
        try
        {
            var result = await Task.Run(() => reader.Read(name, cancellation.Token));
            var final = await Task.Run(reader.Snapshot);
            cancellation.Token.ThrowIfCancellationRequested();
            if (request != generation || closed) return;
            var state = final.FirstOrDefault(x => x.Name == name);
            if (state?.Present != true || state.Revision != initial?.Revision)
                throw new UserVisibleException("The card changed during the read. Please read it again.");
            if (result.Photo is not null)
            {
                try { IdentityView.DecodePhoto(result.Photo); }
                catch (Exception ex) when (ex is IOException or FormatException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException)
                { result = result with { Photo = null }; }
            }
            current = result; CurrentView.ShowIdentity(result);
            ReadResult.Text = "Read complete. Not saved. " + (result.Availability.Length == 0 ? "Choose Save to database to keep this record." : result.Availability + ". You can save the available fields.");
        }
        catch (OperationCanceledException) { if (request == generation) ReadResult.Text = "Reading cancelled or timed out. No data was saved."; }
        catch (Exception ex) { if (request == generation) ReadResult.Text = Friendly(ex); }
        finally
        {
            cancellation.Dispose(); readCancellation = null; reading = false; Buttons();
        }
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    { InvalidateRead(); ReadResult.Text = "Cancelled. Waiting for the reader's current command to finish…"; }
    private void ClearClick(object sender, RoutedEventArgs e)
    { InvalidateRead(); ReadResult.Text = "Cleared. Saved database records are unchanged."; }

    private async void SaveClick(object sender, RoutedEventArgs e)
    {
        if (current is null || saving || deleting || reading) return;
        CheckStorage(); if (!writable) return;
        var identity = current;
        int request = generation;
        saving = true; Buttons();
        try
        {
            await Task.Run(() => repository.Save(identity));
            if (request == generation) ReadResult.Text = "Saved to the local database. An existing record with this MyKad number was updated if present.";
            await LoadRecords();
        }
        catch (Exception ex) { ReadResult.Text = Friendly(ex) + " The displayed read remains available for retry."; CheckStorage(); }
        finally { saving = false; Buttons(); }
    }

    private async Task LoadRecords()
    {
        if (closed) return;
        int request = ++recordGeneration;
        int requestedPage = page; string requestedQuery = query;
        loadingRecords = true; selected = null; SavedView.ShowIdentity(null); Buttons();
        try
        {
            var result = await Task.Run(() => repository.Search(requestedQuery, requestedPage));
            if (request != recordGeneration || closed) return;
            total = result.Total;
            Records.ItemsSource = result.Items;
            PageLabel.Text = $"Page {page + 1} • {total} record(s)";
            RecordsStatus.Text = result.Total == 0 ? "No saved records match. Read a card and choose Save to database to add one." : "Select a record to view its details.";
        }
        catch (Exception ex)
        {
            if (request != recordGeneration || closed) return;
            Records.ItemsSource = null; total = 0; RecordsStatus.Text = Friendly(ex);
        }
        finally { if (request == recordGeneration) { loadingRecords = false; Buttons(); } }
    }

    private async void SearchClick(object sender, RoutedEventArgs e)
    { query = SearchText.Text; page = 0; CheckStorage(); await LoadRecords(); }
    private async void SearchKeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Enter) { query = SearchText.Text; page = 0; await LoadRecords(); } }
    private async void PreviousClick(object sender, RoutedEventArgs e) { if (page > 0) { page--; await LoadRecords(); } }
    private async void NextClick(object sender, RoutedEventArgs e) { if ((page + 1L) * 25 < total) { page++; await LoadRecords(); } }

    private async void RecordSelected(object sender, SelectionChangedEventArgs e)
    {
        selected = null; SavedView.ShowIdentity(null); Buttons();
        if (Records.SelectedItem is not RecordSummary summary) return;
        int request = recordGeneration;
        try
        {
            var record = await Task.Run(() => repository.Get(summary.Number));
            if (closed || request != recordGeneration || !ReferenceEquals(Records.SelectedItem, summary)) return;
            selected = record; SavedView.ShowIdentity(record?.Identity);
            RecordsStatus.Text = record is null ? "Record no longer exists. Search again." : $"Created: {record.CreatedUtc} • Updated: {record.UpdatedUtc} (UTC)";
        }
        catch (Exception ex) { if (request == recordGeneration) RecordsStatus.Text = Friendly(ex); }
        finally { Buttons(); }
    }

    private async void DeleteClick(object sender, RoutedEventArgs e)
    {
        if (selected is null || deleting || saving) return;
        var number = selected.Identity.Number;
        if (MessageBox.Show(this, "Delete this saved record and its photo? This cannot be undone.", "Delete saved record", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        CheckStorage(); if (!writable) return;
        deleting = true; Buttons();
        try { await Task.Run(() => repository.Delete(number)); page = 0; await LoadRecords(); }
        catch (Exception ex) { RecordsStatus.Text = Friendly(ex); CheckStorage(); }
        finally { deleting = false; Buttons(); }
    }

    private static string Friendly(Exception ex) => ex switch
    {
        UserVisibleException or CardCommandException or InvalidDataException => ex.Message,
        SqliteException { SqliteErrorCode: 5 or 6 } => "The database is busy or locked. Close other programs using it and retry.",
        SqliteException { SqliteErrorCode: 11 or 26 } => "The database is damaged or is not a SQLite database. It has not been replaced. Restore a backup with the app closed.",
        SqliteException { SqliteErrorCode: 8 or 14 } or UnauthorizedAccessException => "The database cannot be written or opened. Check folder permissions and reconnect the storage device.",
        SqliteException { SqliteErrorCode: 13 } => "The storage device is full. Free some space and retry.",
        SqliteException => "Database operation failed. The database has not been replaced. Check storage access and retry.",
        IOException => "Storage or image access failed. Check the device and available space, then retry.",
        _ => "The operation failed. Reconnect the device and retry. No diagnostic card data was logged."
    };

    private sealed class SimulatedReader : ICardReader
    {
        public IReadOnlyList<ReaderState> Snapshot() => [new("SIMULATOR — fictional card", true, 0x20)];
        public Identity Read(string reader, CancellationToken cancellation)
        {
            if (cancellation.WaitHandle.WaitOne(600)) cancellation.ThrowIfCancellationRequested();
            return new("000101000000", "FICTIONAL DEMO PERSON", "2000-01-01", "Unavailable", "1 Demo Street\nExample Town", null);
        }
    }
}
