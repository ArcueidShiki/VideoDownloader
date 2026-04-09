using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Windows;
using Microsoft.Win32;

namespace AnyDownloader.Desktop;

public partial class App : Application
{
    private const string MutexName  = "AnyDownloaderSingleInstance";
    private const string PipeName   = "AnyDownloaderIPC";
    private const string ProtocolScheme = "anydownloader";

    private Mutex? _mutex;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        RegisterProtocol();

        string? downloadUrl = ParseProtocolUrl(e.Args);

        _mutex = new Mutex(true, MutexName, out bool isFirstInstance);

        if (!isFirstInstance)
        {
            // App already running — forward URL and exit
            if (!string.IsNullOrEmpty(downloadUrl))
                SendUrlViaPipe(downloadUrl);
            Shutdown();
            return;
        }

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();

        if (!string.IsNullOrEmpty(downloadUrl))
            mainWindow.AddDownloadFromUrl(downloadUrl);

        // Listen for URLs forwarded by subsequent instances
        _ = ListenForPipeUrlsAsync(mainWindow);
    }

    // ── Protocol registration (HKCU — no admin required) ─────────────────────
    private static void RegisterProtocol()
    {
        try
        {
            string exePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;

            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProtocolScheme}");
            key.SetValue("", $"URL:{ProtocolScheme} Protocol");
            key.SetValue("URL Protocol", "");

            using var iconKey = key.CreateSubKey("DefaultIcon");
            iconKey.SetValue("", $"\"{exePath}\",0");

            using var cmdKey = key.CreateSubKey(@"shell\open\command");
            cmdKey.SetValue("", $"\"{exePath}\" \"%1\"");
        }
        catch
        {
            // Non-fatal — protocol just won't be registered this session
        }
    }

    // ── Parse anydownloader://download?url=... from command args ─────────────
    private static string? ParseProtocolUrl(string[] args)
    {
        if (args.Length == 0) return null;

        string arg = args[0];
        if (!arg.StartsWith($"{ProtocolScheme}://", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            var uri   = new Uri(arg);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            return query["url"] is string u && u.Length > 0 ? Uri.UnescapeDataString(u) : null;
        }
        catch
        {
            return null;
        }
    }

    // ── Named-pipe IPC: send URL to running instance ──────────────────────────
    private static void SendUrlViaPipe(string url)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipe.Connect(timeout: 2000);
            using var writer = new StreamWriter(pipe);
            writer.WriteLine(url);
        }
        catch { /* running instance may have closed — ignore */ }
    }

    // ── Named-pipe IPC: receive URLs in the running instance ─────────────────
    private static async Task ListenForPipeUrlsAsync(MainWindow mainWindow)
    {
        while (true)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync();
                using var reader = new StreamReader(pipe);
                string? url = await reader.ReadLineAsync();
                if (!string.IsNullOrWhiteSpace(url))
                    mainWindow.Dispatcher.Invoke(() => mainWindow.AddDownloadFromUrl(url));
            }
            catch
            {
                await Task.Delay(500);
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
