using System.Runtime.InteropServices;
using AnyDownloader.Windows.Services;
using Xunit;

namespace AnyDownloader.Windows.Tests;

/// <summary>
/// Tests for ToolsInstaller — architecture detection and JSON parsing.
/// Network downloads are NOT tested here (requires live GitHub endpoints).
/// </summary>
public class ToolsInstallerTests
{
    // ─────────────────────────────────────────────────────
    // ArchLabel
    // ─────────────────────────────────────────────────────

    [Fact]
    public void ArchLabel_ReturnsNonEmptyString()
    {
        // Whatever arch the test runner uses, label must be a known value
        var label = ToolsInstaller.ArchLabel;
        Assert.Contains(label, new[] { "x64", "x86", "arm64" });
    }

    [Fact]
    public void ProcessArch_MatchesRuntimeInformation()
    {
        Assert.Equal(RuntimeInformation.ProcessArchitecture, ToolsInstaller.ProcessArch);
    }

    // ─────────────────────────────────────────────────────
    // ParseAria2cDownloadUrl
    // ─────────────────────────────────────────────────────

    private const string SampleApiJson = """
        {
          "tag_name": "release-1.37.0",
          "assets": [
            {
              "name": "aria2-1.37.0-win-32bit-build1.zip",
              "browser_download_url": "https://github.com/aria2/aria2/releases/download/release-1.37.0/aria2-1.37.0-win-32bit-build1.zip"
            },
            {
              "name": "aria2-1.37.0-win-64bit-build1.zip",
              "browser_download_url": "https://github.com/aria2/aria2/releases/download/release-1.37.0/aria2-1.37.0-win-64bit-build1.zip"
            },
            {
              "name": "aria2-1.37.0-win-64bit-build1.tar.bz2",
              "browser_download_url": "https://example.com/notazip.tar.bz2"
            }
          ]
        }
        """;

    [Fact]
    public void ParseAria2cDownloadUrl_X64_ReturnsCorrect64BitZip()
    {
        var url = ToolsInstaller.ParseAria2cDownloadUrl(SampleApiJson, Architecture.X64);

        Assert.NotNull(url);
        Assert.Contains("win-64bit", url);
        Assert.EndsWith(".zip", url);
    }

    [Fact]
    public void ParseAria2cDownloadUrl_X86_Returns32BitZip()
    {
        var url = ToolsInstaller.ParseAria2cDownloadUrl(SampleApiJson, Architecture.X86);

        Assert.NotNull(url);
        Assert.Contains("win-32bit", url);
        Assert.EndsWith(".zip", url);
    }

    [Fact]
    public void ParseAria2cDownloadUrl_Arm64_FallsBackTo64BitZip()
    {
        // aria2 has no native arm64 build; the code maps Arm64 → win-64bit tag
        var url = ToolsInstaller.ParseAria2cDownloadUrl(SampleApiJson, Architecture.Arm64);

        Assert.NotNull(url);
        Assert.Contains("win-64bit", url);
        Assert.EndsWith(".zip", url);
    }

    [Fact]
    public void ParseAria2cDownloadUrl_OnlyReturnsZipNotTarBz2()
    {
        // Both win-64bit assets exist but only the .zip should be returned
        var url = ToolsInstaller.ParseAria2cDownloadUrl(SampleApiJson, Architecture.X64);

        Assert.NotNull(url);
        Assert.DoesNotContain(".tar", url);
    }

    [Fact]
    public void ParseAria2cDownloadUrl_EmptyAssets_ReturnsNull()
    {
        const string emptyJson = """{ "tag_name": "v1", "assets": [] }""";
        Assert.Null(ToolsInstaller.ParseAria2cDownloadUrl(emptyJson, Architecture.X64));
    }

    [Fact]
    public void ParseAria2cDownloadUrl_MissingAssetsKey_ReturnsNull()
    {
        const string malformedJson = """{ "tag_name": "v1" }""";
        Assert.Null(ToolsInstaller.ParseAria2cDownloadUrl(malformedJson, Architecture.X64));
    }

    // ─────────────────────────────────────────────────────
    // BinDir
    // ─────────────────────────────────────────────────────

    [Fact]
    public void BinDir_IsUnderLocalAppData()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.StartsWith(localAppData, ToolsInstaller.BinDir, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AnyDownloader", ToolsInstaller.BinDir);
    }
}
