#define MyAppName "ADB USB Speed Test"
#define MyAppVersion "1.6"
#define MyAppExeName "ADB_USB_Speed_Test.exe"

[Setup]
AppId={{8D0FD9E7-ECA9-4B2F-A776-9E1A04DC3DC4}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\ADB USB Speed Test
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=installer
OutputBaseFilename=ADB_USB_Speed_Test_Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Desktop-Verknüpfung erstellen / Create a desktop shortcut"; GroupDescription: "Zusätzliche Aufgaben / Additional tasks:"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "ADB USB Speed Test starten"; Flags: nowait postinstall skipifsilent
