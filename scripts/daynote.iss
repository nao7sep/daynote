; Inno Setup script — builds dist\DayNote-<version>-setup.exe from the
; self-contained win-x64 publish in publish-win\. The version is passed in by
; scripts/package.ps1 via /DMyAppVersion. iscc is pre-installed on windows-latest.

#define MyAppName "DayNote"
#define MyAppPublisher "Yoshinao Inoguchi"
#define MyAppExe "DayNote.exe"
#ifndef MyAppVersion
  #error MyAppVersion is not defined - pass it via  iscc /DMyAppVersion=x.y.z
#endif

[Setup]
; This .iss lives in scripts/, but the win-x64 publish output and the dist/ output
; folder are at the repo root — so resolve all source/output paths one level up.
SourceDir=..
AppName={#MyAppName}
AppId={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExe}
Uninstallable=yes
OutputDir=dist
OutputBaseFilename={#MyAppName}-{#MyAppVersion}-setup
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
SetupIconFile=src\DayNote\icon.ico
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
; Inno Setup's own wizard text for eight of the ten interface languages, English first as the
; fallback. Korean and Simplified Chinese readers get English: Inno ships no wizard text for them,
; and no third-party translation is vendored. The task and launch labels below are Inno's own
; custom messages, which each of these files translates.
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
Name: "it"; MessagesFile: "compiler:Languages\Italian.isl"
Name: "pt_BR"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "ja"; MessagesFile: "compiler:Languages\Japanese.isl"

[Files]
Source: "publish-win\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Run]
; Inno cannot recover a non-elevated user token for every elevated setup path.
; All-users installs launch later through their scoped shell shortcuts.
Filename: "{app}\{#MyAppExe}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent runasoriginaluser; Check: not IsAdminInstallMode
