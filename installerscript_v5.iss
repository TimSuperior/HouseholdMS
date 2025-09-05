#define MyAppName     "HouseholdMS"
#define MyAppVersion  "24"
#define MyAppPublisher "Timur"
#define MyAppURL      "https://www.example.com/"
#define MyAppExeName  "HouseholdMS.exe"
; Set this to your actual Release output (from your screenshot)
#define BuildDir      "E:\ProjectsWork\HouseholdMS\bin\Release"

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
; AnyCPU: allow both archs, install to PF on x64
ArchitecturesAllowed=x86 x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin
DisableProgramGroupPage=yes
OutputBaseFilename=Workbench_Installer_v24
WizardStyle=modern
SolidCompression=yes
Compression=lzma2/ultra

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "korean";  MessagesFile: "compiler:Languages\Korean.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Dirs]
; Your app writes SQLite DB here by default
Name: "{localappdata}\HouseholdMS"

[Files]
; --- Main EXE ---
Source: "{#BuildDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

; --- Bulk copy everything else from Release, EXCEPT items we add explicitly below ---
; (prevents duplicate warnings and ensures correct placement for SQLite interop)
Source: "{#BuildDir}\*"; \
  Excludes: "{#MyAppExeName},System.Data.SQLite.dll,x64\SQLite.Interop.dll,x86\SQLite.Interop.dll"; \
  DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; --- SQLite (managed wrapper) ---
Source: "{#BuildDir}\System.Data.SQLite.dll"; DestDir: "{app}"; Flags: ignoreversion

; --- SQLite native interop: ship BOTH for AnyCPU ---
Source: "{#BuildDir}\x64\SQLite.Interop.dll"; DestDir: "{app}\x64"; Flags: ignoreversion
Source: "{#BuildDir}\x86\SQLite.Interop.dll"; DestDir: "{app}\x86"; Flags: ignoreversion

; --- Optional: seed DB (your app creates it if missing) ---
; Source: "{#BuildDir}\household_management.db"; DestDir: "{localappdata}\HouseholdMS"; Flags: onlyifdoesntexist ignoreversion

; --- Optional: VC++ 2015–2022 redists (place EXEs in BuildDir if you need them) ---
; Source: "{#BuildDir}\vc_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: NeedVCRedistX64
; Source: "{#BuildDir}\vc_redist.x86.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: NeedVCRedistX86

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}";  Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Install VC++ redists if you enabled them above
; Filename: "{tmp}\vc_redist.x64.exe"; Parameters: "/install /quiet /norestart"; Flags: waituntilterminated; Check: NeedVCRedistX64
; Filename: "{tmp}\vc_redist.x86.exe"; Parameters: "/install /quiet /norestart"; Flags: waituntilterminated; Check: NeedVCRedistX86
; Launch app after install
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
function IsVCRedistInstalled(Arch64: Boolean): Boolean;
var
  val: Cardinal;
  Key: string;
begin
  if Arch64 then
    Key := 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64'
  else
    Key := 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x86';

  if RegQueryDWordValue(HKLM, Key, 'Installed', val) then
    Result := (val = 1)
  else
  begin
    if Arch64 then
      Key := 'SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\x64'
    else
      Key := 'SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\x86';

    if RegQueryDWordValue(HKLM, Key, 'Installed', val) then
      Result := (val = 1)
    else
      Result := False;
  end;
end;

function NeedVCRedistX64: Boolean;
begin
  Result := Is64BitInstallMode and (not IsVCRedistInstalled(True));
end;

function NeedVCRedistX86: Boolean;
begin
  Result := not IsVCRedistInstalled(False);
end;
