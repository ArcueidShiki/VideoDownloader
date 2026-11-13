<#
.SYNOPSIS
  One-click Windows x64 release build for AnyDownloader.

.DESCRIPTION
  1. Downloads the bundled tools (yt-dlp, Deno, FFmpeg/ffprobe, aria2c) into
     windows\tools\ and verifies SHA-256 against upstream checksum files.
  2. Runs the Windows unit tests (skip with -SkipTests).
  3. dotnet publish (self-contained, unpackaged WinUI) -> windows\publish\x64.
  4. Inno Setup -> release\windows\AnyDownloader-<ver>-x64-setup.exe
     plus a portable zip of the publish folder.

  Must run on Windows: the WinUI XAML compiler is a .NET Framework executable.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\build-windows.ps1
  powershell -ExecutionPolicy Bypass -File scripts\build-windows.ps1 -RefreshTools -SkipTests
#>
[CmdletBinding()]
param(
    [switch]$RefreshTools,   # re-download tools even when present
    [switch]$SkipTests,
    [switch]$SkipInstaller   # only produce the portable zip
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference    = 'SilentlyContinue'   # Invoke-WebRequest is 10x faster without it

$Root       = Split-Path -Parent $PSScriptRoot
$WinDir     = Join-Path $Root 'windows'
$ToolsDir   = Join-Path $WinDir 'tools'
$PublishDir = Join-Path $WinDir 'publish\x64'
$OutDir     = Join-Path $Root 'release\windows'
$WorkDir    = Join-Path ([IO.Path]::GetTempPath()) ("anydl-tools-" + [guid]::NewGuid())
$UserAgent  = 'Mozilla/5.0'

# aria2 publishes no checksum file; release-1.37.0 is immutable, so its hash is pinned
# (computed 2026-11-13 from the official GitHub release asset).
$Aria2Url    = 'https://github.com/aria2/aria2/releases/download/release-1.37.0/aria2-1.37.0-win-64bit-build1.zip'
$Aria2Sha256 = '67d015301eef0b612191212d564c5bb0a14b5b9c4796b76454276a4d28d9b288'

function Write-Step([string]$Message) { Write-Host "`n==> $Message" -ForegroundColor Cyan }

function Assert-Windows {
    if ($env:OS -ne 'Windows_NT') {
        throw 'build-windows.ps1 must run on Windows (WinUI XAML compiler is Windows-only). On macOS use scripts/build-release.sh, which dispatches the Windows CI build.'
    }
}

function Get-Download([string]$Url, [string]$Destination) {
    Invoke-WebRequest -Uri $Url -OutFile $Destination -UserAgent $UserAgent -UseBasicParsing
    if (-not (Test-Path $Destination) -or (Get-Item $Destination).Length -eq 0) {
        throw "Download failed or empty: $Url"
    }
}

function Get-Text([string]$Url) {
    # GitHub serves checksum files as application/octet-stream, so Content is byte[].
    # Never pipe it: the pipeline would enumerate it byte by byte.
    $content = (Invoke-WebRequest -Uri $Url -UserAgent $UserAgent -UseBasicParsing).Content
    if ($content -is [byte[]]) { return [Text.Encoding]::UTF8.GetString($content) }
    return [string]$content
}

# Finds "<64 hex>  <fileName>" in a sha256sum-style list; also accepts a bare hash line
# or PowerShell Get-FileHash output ("Hash : ABC...") when $FileName is $null.
function Find-Sha256([string]$Text, [string]$FileName) {
    foreach ($line in ($Text -split "`r?`n")) {
        if ($FileName -and $line -match '^([0-9a-fA-F]{64})\s+\*?(.+)$' -and $Matches[2].Trim() -eq $FileName) {
            return $Matches[1].ToLowerInvariant()
        }
        if (-not $FileName -and $line -match '([0-9a-fA-F]{64})') { return $Matches[1].ToLowerInvariant() }
    }
    throw "No SHA-256 for '$FileName' in checksum list"
}

function Assert-Sha256([string]$Path, [string]$Expected) {
    $actual = (Get-FileHash -Algorithm SHA256 -Path $Path).Hash.ToLowerInvariant()
    if ($actual -ne $Expected) { throw "SHA-256 mismatch for $(Split-Path -Leaf $Path): expected $Expected, got $actual" }
    Write-Host "    sha256 OK  $(Split-Path -Leaf $Path)"
}

function Expand-Executables([string]$Zip, [string[]]$Names) {
    $dest = Join-Path $WorkDir ([IO.Path]::GetFileNameWithoutExtension($Zip))
    Expand-Archive -Path $Zip -DestinationPath $dest -Force
    foreach ($name in $Names) {
        $found = Get-ChildItem -Path $dest -Recurse -Filter $name | Select-Object -First 1
        if (-not $found) { throw "$name not found inside $(Split-Path -Leaf $Zip)" }
        Copy-Item $found.FullName (Join-Path $ToolsDir $name) -Force
    }
}

function Install-Tools {
    $needed = 'yt-dlp.exe', 'deno.exe', 'ffmpeg.exe', 'ffprobe.exe', 'aria2c.exe'
    $missing = $needed | Where-Object { -not (Test-Path (Join-Path $ToolsDir $_)) }
    if (-not $RefreshTools -and -not $missing) { Write-Host '    all tools present (use -RefreshTools to update)'; return }
    New-Item -ItemType Directory -Force -Path $WorkDir, $ToolsDir | Out-Null

    # yt-dlp: always the latest stable — site extractors (Bilibili, YouTube) break often.
    $ytdlp = Join-Path $ToolsDir 'yt-dlp.exe'
    Get-Download 'https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe' $ytdlp
    Assert-Sha256 $ytdlp (Find-Sha256 (Get-Text 'https://github.com/yt-dlp/yt-dlp/releases/latest/download/SHA2-256SUMS') 'yt-dlp.exe')

    $denoZip = Join-Path $WorkDir 'deno.zip'
    Get-Download 'https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip' $denoZip
    Assert-Sha256 $denoZip (Find-Sha256 (Get-Text 'https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip.sha256sum') $null)
    Expand-Executables $denoZip @('deno.exe')

    $ffName = 'ffmpeg-master-latest-win64-gpl.zip'
    $ffZip  = Join-Path $WorkDir $ffName
    Get-Download "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/$ffName" $ffZip
    Assert-Sha256 $ffZip (Find-Sha256 (Get-Text 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/checksums.sha256') $ffName)
    Expand-Executables $ffZip @('ffmpeg.exe', 'ffprobe.exe')

    $ariaZip = Join-Path $WorkDir 'aria2.zip'
    Get-Download $Aria2Url $ariaZip
    Assert-Sha256 $ariaZip $Aria2Sha256
    Expand-Executables $ariaZip @('aria2c.exe')

    Write-Host "    yt-dlp $(& $ytdlp --version)"
}

function Find-Iscc {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($base in @(${env:ProgramFiles(x86)}, $env:ProgramFiles, "$env:LOCALAPPDATA\Programs")) {
        if (-not $base) { continue }
        $candidate = Join-Path $base 'Inno Setup 6\ISCC.exe'
        if (Test-Path $candidate) { return $candidate }
    }
    return $null
}

function Invoke-Checked([string]$What, [scriptblock]$Command) {
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$What failed (exit $LASTEXITCODE)" }
}

# ── main ────────────────────────────────────────────────────────────────────
try {
    Assert-Windows
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET SDK not found. Install the SDK in global.json (9.0.x) plus the .NET 8 runtime.' }
    $versionNode = ([xml](Get-Content -Raw (Join-Path $WinDir 'AnyDownloader.Windows.csproj'))).SelectSingleNode('//Version')
    if (-not $versionNode) { throw 'No <Version> in AnyDownloader.Windows.csproj' }
    $version = $versionNode.InnerText.Trim()

    Write-Step "AnyDownloader for Windows $version (x64)"
    Write-Step 'Bundled tools'
    Install-Tools

    if (-not $SkipTests) {
        Write-Step 'Unit tests'
        Invoke-Checked 'dotnet test' {
            dotnet test (Join-Path $WinDir 'Tests\AnyDownloader.Windows.Tests.csproj') -c Release `
                --filter 'FullyQualifiedName!~E2e&FullyQualifiedName!~Live'
        }
    }

    Write-Step 'dotnet publish (Release, self-contained)'
    if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
    Invoke-Checked 'dotnet publish' {
        dotnet publish (Join-Path $WinDir 'AnyDownloader.Windows.csproj') -c Release -r win-x64 `
            --self-contained true -p:Platform=x64 -p:WindowsPackageType=None `
            -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false `
            -p:EnableMsixTooling=true -o $PublishDir
    }
    foreach ($required in 'AnyDownloader.Windows.exe', 'yt-dlp.exe', 'deno.exe', 'ffmpeg.exe', 'ffprobe.exe', 'aria2c.exe') {
        if (-not (Get-ChildItem -Path $PublishDir -Recurse -Filter $required | Select-Object -First 1)) {
            throw "Publish output is missing $required"
        }
    }

    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    Write-Step 'Portable zip'
    $zip = Join-Path $OutDir "AnyDownloader-$version-x64-portable.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $PublishDir '*') -DestinationPath $zip
    Write-Host "    $zip"

    if (-not $SkipInstaller) {
        Write-Step 'Inno Setup installer'
        $iscc = Find-Iscc
        if (-not $iscc) { throw 'Inno Setup 6 not found. Install it (winget install JRSoftware.InnoSetup) or pass -SkipInstaller.' }
        Push-Location $WinDir
        try { Invoke-Checked 'ISCC' { & $iscc 'installer.iss' '/DArch=x64' "/DPublishDir=$PublishDir" "/O$OutDir" } }
        finally { Pop-Location }
    }

    Write-Step 'Done'
    Get-ChildItem $OutDir | ForEach-Object { Write-Host ("    {0}  ({1:N1} MB)" -f $_.FullName, ($_.Length / 1MB)) }
}
catch {
    Write-Host "`nBUILD FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
finally {
    if (Test-Path $WorkDir) { Remove-Item $WorkDir -Recurse -Force -ErrorAction SilentlyContinue }
}
