Windows x64 bundled tools - verified 2026-10-06 (app 1.1.0)

Bundled dependencies (executables are build artifacts, excluded from git):
yt-dlp 2026.08.19: https://github.com/yt-dlp/yt-dlp/releases/tag/2026.08.19
Deno 2.9.7: https://github.com/denoland/deno/releases/tag/v2.9.7
FFmpeg/ffprobe N-127203-ga35c879992-20261005:
https://github.com/BtbN/FFmpeg-Builds/releases/tag/latest
aria2 1.37.0: https://github.com/aria2/aria2/releases/tag/release-1.37.0

yt-dlp's official Windows executable includes its EJS scripts. Deno is selected
explicitly so JavaScript-dependent extraction works without a system runtime.
The application uses ffmpeg/ffprobe from this directory for stream merging.
Licenses and third-party notices are in licenses/.

Build (requires .NET SDK matching global.json; WinUI tooling is restored by NuGet):
dotnet publish windows/AnyDownloader.Windows.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false -p:EnableMsixTooling=true -o windows/publish/x64
Then use Inno Setup 6: ISCC windows/installer.iss /DArch=x64

Run local unit tests:
dotnet test windows/Tests/AnyDownloader.Windows.Tests.csproj --filter FullyQualifiedName!~E2e
Opt-in public-media tests: set ANYDOWNLOADER_LIVE_YTDLP to the full executable path,
add the bundled tools directory to PATH, and filter FullyQualifiedName~YtDlpLiveTests.
Network tests must only use media that the tester has permission to download.
