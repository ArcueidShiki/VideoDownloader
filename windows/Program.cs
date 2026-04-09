using System;
using Microsoft.UI.Xaml;

namespace AnyDownloader.Windows;

/// <summary>
/// Custom entry point (DISABLE_XAML_GENERATED_MAIN suppresses the generated one).
///
/// Debug builds are self-contained (WindowsAppSDKSelfContained=true): all
/// Windows App Runtime DLLs are copied next to the exe at build time.
/// The Windows DLL loader finds them automatically via the app-local path —
/// Bootstrap.Initialize() must NOT be called; it checks the system package
/// registry which has nothing to do with self-contained DLLs, and throws
/// 0x80670016 even when every required DLL is present.
///
/// Release builds are MSIX-packaged: the runtime is declared as a package
/// dependency and installed by Windows automatically.
/// </summary>
internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Self-contained: runtime DLLs are already in the output folder.
        // They are loaded by Windows automatically — no bootstrap call needed.
        Application.Start(_ => new App());
    }
}
