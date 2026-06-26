; Orynivo Inno Setup installer script
; Compile locally:  ISCC.exe /dAppVersion=0.13.0 installer\Orynivo.iss
; CI sets AppVersion via the /dAppVersion command-line define.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#define AppName      "Orynivo"
#define AppPublisher "Björn Schlaack"
#define AppURL       "https://github.com/bschlaack/Orynivo"
#define AppExe       "Orynivo.exe"
; Fixed GUID — must never change between releases so upgrades work correctly.
#define AppId        "{{894E3E02-3CB4-4614-917A-12F8A7796571}"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Audio Player

; Installation directory
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes

; Minimum supported Windows version: Windows 10 2004 (build 19041)
MinVersion=10.0.19041
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Appearance
WizardStyle=modern
SetupIconFile=..\Orynivo\Assets\Orynivo.ico
UninstallDisplayIcon={app}\{#AppExe}
LicenseFile=..\LICENSE

; Output
OutputDir=Output
OutputBaseFilename=Orynivo-{#AppVersion}-win-x64-Setup

; Compression
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes

; Privilege
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german";  MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; \
  Description: "{cm:CreateDesktopIcon}"; \
  GroupDescription: "{cm:AdditionalIcons}"; \
  Flags: unchecked

[Files]
; All published application files (self-contained, no .NET prerequisite needed)
Source: "..\artifacts\Orynivo-win-x64\*"; \
  DestDir: "{app}"; \
  Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}";    Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; \
  Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; \
  Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Remove files downloaded at runtime (ffmpeg binaries) during uninstall.
; User library data under %LOCALAPPDATA%\Orynivo\ is intentionally preserved.
Type: filesandordirs; Name: "{app}\ffmpeg"
