#define MyAppName "HouseholdMS"
#define MyAppVersion "6"
#define MyAppPublisher "Timur"
#define MyAppURL "https://www.example.com/"
#define MyAppExeName "HouseholdMS.exe"

[Setup]
AppId={{C4FD1D77-6132-408B-997B-A0DF717715AB}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
DisableProgramGroupPage=yes
OutputBaseFilename=Workbench_Installer_v6
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Main app exe and all required files (dlls, configs, etc.)
Source: "E:\Deploy\Insataller\Release\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "E:\Deploy\Insataller\Release\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Optional: include a default config file if needed
Source: "E:\Deploy\Insataller\ConfigurationFile.ini"; DestDir: "{app}"; Flags: ignoreversion
; Optional: include a pre-seeded blank SQLite DB (your app will create if missing)
; Source: "E:\Deploy\Insataller\household_management.db"; DestDir: "{userappdata}\HouseholdMS"; Flags: onlyifdoesntexist

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Launch your app after install (optional)
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
