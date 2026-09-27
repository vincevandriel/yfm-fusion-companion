using System.Windows;
using System.IO;
using System.Text.Json;

namespace YfmCompanion.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length == 2 && e.Args[0] == "--verify-package")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                var report = PackageVerifier.Verify(AppContext.BaseDirectory);
                File.WriteAllText(e.Args[1], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                Shutdown(0);
            }
            catch (Exception error)
            {
                try { File.WriteAllText(e.Args[1], JsonSerializer.Serialize(new { Passed = false, Error = error.Message })); }
                catch (IOException) { Console.Error.WriteLine(error.Message); }
                catch (UnauthorizedAccessException) { Console.Error.WriteLine(error.Message); }
                finally { Shutdown(1); }
            }
            return;
        }
        StartupUri = new Uri("MainWindow.xaml", UriKind.Relative);
        base.OnStartup(e);
    }
}
