; Inno Setup-script voor PodPilot (stap 8).
; Eerst publiceren (zie build.ps1), daarna: ISCC.exe installer\PodPilot.iss
; Resultaat: installer\output\PodPilot-Setup-<versie>.exe
;
; Installatie per gebruiker (geen beheerdersrechten), net als de app zelf: die schrijft de
; Startmenu-snelkoppeling en de opstartregel ook alleen voor de huidige gebruiker.

#define AppName "PodPilot"
#define AppExe "PodPilot.exe"
#define AppSource "..\publish\" + AppExe
; Versie komt uit de exe (<Version> in PodPilot.App.csproj).
#define AppVersion GetVersionNumbersString(AddBackslash(SourcePath) + AppSource)
; Moet gelijk zijn aan StartMenuShortcut.AppUserModelId, anders werken de meldingsbanners niet.
#define AppUserModelId "PodPilot.App"

[Setup]
AppId={{6F2B8C1E-4D7A-4E59-9B3F-2A1C5D8E7F40}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir=output
OutputBaseFilename={#AppName}-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExe}
; Zelfde icoon als de app, ook voor de installer zelf.
SetupIconFile=..\src\PodPilot.App\Assets\PodPilot.ico
UninstallDisplayName={#AppName}
; Een draaiende PodPilot (zit in het systeemvak, heeft geen venster) netjes laten afsluiten.
CloseApplications=force
RestartApplications=no

[Languages]
Name: "dutch"; MessagesFile: "compiler:Languages\Dutch.isl"

[Tasks]
Name: "startup"; Description: "PodPilot starten bij het aanmelden bij Windows"; GroupDescription: "Opstarten:"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#AppSource}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Dezelfde snelkoppeling (naam, plek en AUMID) die de app bij het opstarten zelf bijwerkt.
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Comment: "AirPods-batterij en verbinding"; AppUserModelID: "{#AppUserModelId}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
; Zelfde waarde als StartupRegistration.cs, zodat de schakelaar in de instellingen klopt.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#AppExe}"""; Tasks: startup

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; De app draait in het systeemvak; eerst stoppen, anders kan de exe niet weg.
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#AppExe} /F"; Flags: runhidden; RunOnceId: "StopPodPilot"

[UninstallDelete]
; Diagnoselog. De instellingen in %AppData%\PodPilot blijven staan (handig bij opnieuw installeren).
Type: filesandordirs; Name: "{localappdata}\{#AppName}"

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
  // Taak "starten met Windows" uitgevinkt: een eerder gezette opstartregel ook weghalen.
  if (CurStep = ssPostInstall) and not WizardIsTaskSelected('startup') then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#AppName}');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  // De app kan de opstartregel zelf gezet hebben (instellingenvenster); die altijd opruimen.
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#AppName}');
end;
