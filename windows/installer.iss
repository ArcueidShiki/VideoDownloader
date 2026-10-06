; AnyDownloader — Inno Setup installer script
; Usage:
;   x64:   ISCC installer.iss /DArch=x64
;   x86:   ISCC installer.iss /DArch=x86
;   arm64: ISCC installer.iss /DArch=arm64

#ifndef Arch
  #define Arch "x64"
#endif

#define AppName      "AnyDownloader"
#define AppVersion   "1.1.0"
#define AppPublisher "AnyDownloader"
#define AppExeName   "AnyDownloader.Windows.exe"
#ifndef PublishDir
  #define PublishDir "publish\" + Arch
#endif

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\{#AppName}
PrivilegesRequired=lowest
DefaultGroupName={#AppName}
OutputDir=dist
OutputBaseFilename=AnyDownloader-{#AppVersion}-{#Arch}-setup
Compression=lzma2/ultra64
SolidCompression=yes
#if Arch == "x64"
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64
#elif Arch == "arm64"
ArchitecturesInstallIn64BitMode=arm64
ArchitecturesAllowed=arm64
#else
ArchitecturesInstallIn64BitMode=
ArchitecturesAllowed=x86 x64
#endif
; Require Windows 10 1809+
MinVersion=10.0.17763
WizardStyle=modern
SetupIconFile=icons\icon_128x128.ico
UninstallDisplayIcon={app}\{#AppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce

[Files]
; WinUI and .NET self-contained apps require the complete publish directory.
; Keep tools\ intact: BinaryResolver looks there, including the bundled JS runtime.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}";           Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall";            Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}";     Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
