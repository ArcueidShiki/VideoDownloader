using Microsoft.UI.Xaml;
using AnyDownloader.Windows.Services;
using AnyDownloader.Windows.Views;

namespace AnyDownloader.Windows;

/// <summary>
/// Entry point for the WinUI3 application.
///
/// Launch sequence:
///   1. If all required tools are present  → open MainWindow directly.
///   2. If any tool is missing             → open SetupWindow first.
///      SetupWindow calls <see cref="CompleteSetup"/> when done,
///      which closes the setup window and opens MainWindow.
///
/// Exposes <see cref="MainWindow"/> so dialogs can obtain the HWND for WinRT pickers.
/// </summary>
public partial class App : Application
{
    /// <summary>The main application window (set after setup completes or on direct launch).</summary>
    public MainWindow? MainWindow { get; private set; }

    private SetupWindow? _setupWindow;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (ToolsInstaller.AllInstalled())
        {
            OpenMainWindow();
        }
        else
        {
            _setupWindow = new SetupWindow();
            _setupWindow.Activate();
        }
    }

    /// <summary>
    /// Called by <see cref="SetupWindow"/> when all tools have been installed.
    /// Closes the setup window and opens the main application window.
    /// </summary>
    public void CompleteSetup()
    {
        _setupWindow?.Close();
        _setupWindow = null;
        OpenMainWindow();
    }

    private void OpenMainWindow()
    {
        MainWindow = new MainWindow();
        MainWindow.Activate();
    }
}
