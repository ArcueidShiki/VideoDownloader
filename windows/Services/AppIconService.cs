using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace AnyDownloader.Windows.Services;

/// <summary>
/// Builds the application .ico from the real PNG icons in the icons\ folder
/// and exposes a Win32 helper to set both the title-bar AND taskbar icon.
///
/// Icon source priority:
///   1. icons\icon_256x256.png, icons\icon_32x32.png, icons\icon_16x16.png
///      (copied to the output directory by the .csproj Content items)
///   2. Fallback: hand-drawn 32×32 pixel art circle+arrow (no external files needed)
///
/// ICO format: multi-frame with raw PNG chunks (Vista+ feature).
/// Each ICONDIRENTRY points to the raw bytes of the PNG file.
///
/// The .ico is cached at %LOCALAPPDATA%\AnyDownloader\appicon-v3.ico.
/// </summary>
public static class AppIconService
{
    private static readonly string IconPath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AnyDownloader", "appicon-v4.ico");

    // PNG files from the icons\ folder next to the executable
    private static readonly (int Size, string File)[] _pngSources =
    [
        (256, "icons/icon_256x256.png"),
        ( 32, "icons/icon_32x32.png"),
        ( 16, "icons/icon_16x16.png"),
    ];

    // ─────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns the path to the .ico file, generating it on first call.
    /// Returns null on failure; callers must check before using.
    /// </summary>
    public static Task<string?> GetOrCreateAsync()
    {
        return Task.Run<string?>(() =>
        {
            try
            {
                if (!File.Exists(IconPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(IconPath)!);
                    var icoBytes = BuildIco();
                    File.WriteAllBytes(IconPath, icoBytes);
                }
                return IconPath;
            }
            catch
            {
                return null;
            }
        });
    }

    /// <summary>
    /// Sets both the title-bar icon (AppWindow) AND the taskbar icon (Win32 WM_SETICON)
    /// for the given window handle.  Call this after <c>AppWindow.SetIcon(path)</c>.
    /// </summary>
    public static void SetHwndIcon(IntPtr hwnd, string iconPath)
    {
        try
        {
            // System large icon size (SM_CXICON = 11); usually 32 or 48 px
            int bigPx = NativeMethods.GetSystemMetrics(11);
            if (bigPx <= 0) bigPx = 32;

            var hBig = NativeMethods.LoadImage(
                IntPtr.Zero, iconPath, NativeMethods.IMAGE_ICON,
                bigPx, bigPx, NativeMethods.LR_LOADFROMFILE);

            var hSmall = NativeMethods.LoadImage(
                IntPtr.Zero, iconPath, NativeMethods.IMAGE_ICON,
                16, 16, NativeMethods.LR_LOADFROMFILE);

            // WM_SETICON: updates title-bar + Alt-Tab thumbnail
            if (hBig   != IntPtr.Zero)
                NativeMethods.SendMessage(hwnd, NativeMethods.WM_SETICON, (IntPtr)1, hBig);
            if (hSmall != IntPtr.Zero)
                NativeMethods.SendMessage(hwnd, NativeMethods.WM_SETICON, (IntPtr)0, hSmall);

            // SetClassLongPtr: updates the ENTIRE window class, which is what the
            // Windows taskbar button reads.  WM_SETICON alone is not enough.
            if (hBig   != IntPtr.Zero)
                NativeMethods.SetClassLongPtr(hwnd, NativeMethods.GCLP_HICON,   hBig);
            if (hSmall != IntPtr.Zero)
                NativeMethods.SetClassLongPtr(hwnd, NativeMethods.GCLP_HICONSM, hSmall);
        }
        catch { /* P/Invoke failures are non-fatal */ }
    }

    /// <summary>
    /// Returns the path to the real icon PNG, searching the deployed output folder and
    /// the dev source tree.  Used by MainWindow to call AppWindow.SetIcon(pngPath)
    /// directly — WinAppSDK 1.5 accepts PNG for the title-bar icon.
    /// </summary>
    public static string? FindSourcePng()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "icons", "icon_256x256.png"),
            Path.Combine(
                Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly()?.Location ?? "") ?? "",
                "icons", "icon_256x256.png"),
            @"e:\github\projects\AnyDownloader\windows\icons\icon_256x256.png",
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    // ─────────────────────────────────────────────────────
    // ICO builder — PNG-in-ICO (Vista+ format)
    // ─────────────────────────────────────────────────────
    //
    // Modern ICO can store each frame as a raw PNG blob.  Windows picks the
    // closest size at runtime.  This is much simpler than converting to BGRA.
    //
    // Layout:
    //   [6]       ICONDIR   header
    //   [N × 16]  ICONDIRENTRY
    //   for each frame: raw PNG bytes (starting with \x89PNG)

    private static byte[] BuildIco()
    {
        // Collect available PNG frames — try the deployed output dir first, then dev source
        var frames = new System.Collections.Generic.List<(int size, byte[] png)>();

        // Candidate base directories: output dir, entry assembly dir, dev source
        var candidateDirs = new[]
        {
            AppContext.BaseDirectory,
            Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly()?.Location ?? "") ?? string.Empty,
            @"e:\github\projects\AnyDownloader\windows",   // dev source fallback
        };

        foreach (var (size, rel) in _pngSources)
        {
            foreach (var baseDir in candidateDirs)
            {
                if (string.IsNullOrEmpty(baseDir)) continue;
                var full = Path.Combine(baseDir, rel.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(full))
                {
                    frames.Add((size, File.ReadAllBytes(full)));
                    break; // found this size, move to next
                }
            }
        }

        // Fallback to hand-drawn pixel art if no PNG files were found
        if (frames.Count == 0)
        {
            var fallbackPng = BuildFallbackPngInIco();
            return fallbackPng;
        }

        return BuildPngInIco(frames);
    }

    private static byte[] BuildPngInIco(
        System.Collections.Generic.List<(int size, byte[] png)> frames)
    {
        int n = frames.Count;
        int totalSize = 6 + n * 16;
        foreach (var (_, png) in frames) totalSize += png.Length;

        var ico = new byte[totalSize];
        int pos = 0;

        // ICONDIR
        Write16(ico, ref pos, 0); // reserved
        Write16(ico, ref pos, 1); // type = ICO
        Write16(ico, ref pos, n);

        // Calculate where image data starts
        int dataOffset = 6 + n * 16;

        // ICONDIRENTRY × n
        // For PNG-in-ICO: planes and bitCount MUST both be 0 (per the ICO spec for PNG entries).
        // Using planes=1 / bitCount=32 here causes LoadImage() to misinterpret the frame type.
        foreach (var (size, png) in frames)
        {
            // For 256, the spec uses 0 in width/height fields
            ico[pos++] = (byte)(size >= 256 ? 0 : size);
            ico[pos++] = (byte)(size >= 256 ? 0 : size);
            ico[pos++] = 0;  // colorCount
            ico[pos++] = 0;  // reserved
            Write16(ico, ref pos, 0);          // planes  = 0 for PNG entries
            Write16(ico, ref pos, 0);          // bitCount = 0 for PNG entries
            Write32(ico, ref pos, png.Length); // image data size
            Write32(ico, ref pos, dataOffset); // file offset of image data
            dataOffset += png.Length;
        }

        // Raw PNG bytes for each frame
        foreach (var (_, png) in frames)
        {
            Array.Copy(png, 0, ico, pos, png.Length);
            pos += png.Length;
        }

        return ico;
    }

    // ─────────────────────────────────────────────────────
    // Fallback: hand-drawn 32×32 BMP-based ICO
    // (used only when the icons\ folder is not present)
    // ─────────────────────────────────────────────────────

    private static byte[] BuildFallbackPngInIco()
    {
        var frames = new[] { (32, GeneratePixels(32)), (16, GeneratePixels(16)) };

        int n = frames.Length;
        int totalSize = 6 + n * 16;
        foreach (var (s, _) in frames)
            totalSize += 40 + s * s * 4 + s * s / 8;

        var ico = new byte[totalSize];
        int pos = 0;

        Write16(ico, ref pos, 0);
        Write16(ico, ref pos, 1);
        Write16(ico, ref pos, n);

        int dataOffset = 6 + n * 16;
        foreach (var (s, _) in frames)
        {
            int imgSize = 40 + s * s * 4 + s * s / 8;
            ico[pos++] = (byte)s;
            ico[pos++] = (byte)s;
            ico[pos++] = 0;
            ico[pos++] = 0;
            Write16(ico, ref pos, 1);
            Write16(ico, ref pos, 32);
            Write32(ico, ref pos, imgSize);
            Write32(ico, ref pos, dataOffset);
            dataOffset += imgSize;
        }

        foreach (var (s, px) in frames)
        {
            int andBytes = s * s / 8;
            Write32(ico, ref pos, 40);    // biSize
            Write32(ico, ref pos, s);     // biWidth
            Write32(ico, ref pos, s * 2); // biHeight (XOR+AND)
            Write16(ico, ref pos, 1);     // biPlanes
            Write16(ico, ref pos, 32);    // biBitCount
            Write32(ico, ref pos, 0);     // biCompression
            Write32(ico, ref pos, 0);
            Write32(ico, ref pos, 0);
            Write32(ico, ref pos, 0);
            Write32(ico, ref pos, 0);
            Write32(ico, ref pos, 0);

            for (int row = s - 1; row >= 0; row--)
            {
                int srcBase = row * s * 4;
                for (int col = 0; col < s; col++)
                {
                    int src = srcBase + col * 4;
                    ico[pos++] = px[src + 0];
                    ico[pos++] = px[src + 1];
                    ico[pos++] = px[src + 2];
                    ico[pos++] = px[src + 3];
                }
            }
            pos += andBytes; // AND mask (already zero)
        }

        return ico;
    }

    private static byte[] GeneratePixels(int s)
    {
        var px = new byte[s * s * 4];
        double cx = (s - 1) / 2.0, cy = (s - 1) / 2.0, r = s * 0.46;
        const byte bgB = 0xC0, bgG = 0x65, bgR = 0x15;

        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            double dx = x - cx, dy = y - cy;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist > r + 0.5) continue;

            double alpha = dist > r - 0.5 ? (r + 0.5 - dist) : 1.0;
            byte a = (byte)(alpha * 255 + 0.5);
            bool arrow = IsArrow(x, y, s, cx);
            int i = (y * s + x) * 4;
            px[i] = arrow ? (byte)255 : bgB;
            px[i + 1] = arrow ? (byte)255 : bgG;
            px[i + 2] = arrow ? (byte)255 : bgR;
            px[i + 3] = a;
        }
        return px;
    }

    private static bool IsArrow(int x, int y, int s, double cx)
    {
        if (Math.Abs(x - cx) <= s * 0.10 && y >= s * 0.22 && y < s * 0.52) return true;
        if (y >= s * 0.52 && y < s * 0.72)
        {
            double t = (y - s * 0.52) / (s * 0.20);
            if (Math.Abs(x - cx) <= s * 0.10 + t * s * 0.25 + 0.5) return true;
        }
        if (y >= s * 0.76 && y < s * 0.84 && Math.Abs(x - cx) <= s * 0.35) return true;
        return false;
    }

    // ─────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────

    private static void Write16(byte[] b, ref int pos, int v)
    {
        b[pos++] = (byte)v;
        b[pos++] = (byte)(v >> 8);
    }

    private static void Write32(byte[] b, ref int pos, int v)
    {
        b[pos++] = (byte)v;
        b[pos++] = (byte)(v >> 8);
        b[pos++] = (byte)(v >> 16);
        b[pos++] = (byte)(v >> 24);
    }

    // ─────────────────────────────────────────────────────
    // Win32 P/Invoke (icon setting for taskbar)
    // ─────────────────────────────────────────────────────

    private static class NativeMethods
    {
        internal const uint WM_SETICON      = 0x0080;
        internal const uint IMAGE_ICON      = 1;
        internal const uint LR_LOADFROMFILE = 0x0010;
        internal const int  GCLP_HICON      = -14;
        internal const int  GCLP_HICONSM    = -34;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        internal static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern IntPtr LoadImage(
            IntPtr hinst, string lpszName, uint uType,
            int cxDesired, int cyDesired, uint fuLoad);

        [DllImport("user32.dll")]
        internal static extern int GetSystemMetrics(int nIndex);

        // Sets icon on the window CLASS — required for taskbar button to update
        [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
        internal static extern IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    }
}
