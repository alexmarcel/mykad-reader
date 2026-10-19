using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;

namespace MyKadReader;

public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var path = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
        instance = new Mutex(true, "Local\\MyKadReader-" + key, out bool acquired);
        if (!acquired)
        {
            MessageBox.Show("This portable app folder is already open. Switch to its existing window.", "MyKad Reader");
            instance.Dispose(); instance = null; Shutdown(); return;
        }
        bool simulate = false;
#if DEBUG
        simulate = e.Args.Contains("--simulate");
#endif
        MainWindow = new MainWindow(simulate);
        MainWindow.Show();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        if (instance is not null) { instance.ReleaseMutex(); instance.Dispose(); }
        base.OnExit(e);
    }
}
