; Installeur Windows de Barcode Board (Inno Setup 6).
; Nom affiché selon la langue de l'installation : « Tableau de code-barres » en français, « Barcode Board »
; en anglais. Le dossier d'installation, l'exe et l'association des fichiers gardent le nom technique.
; Installation par utilisateur, sans droits administrateur.
; Compilation : ISCC.exe /DAppVersion=1.2.3 installer\BarcodeBoard.iss
; (l'exe autonome doit d'abord être publié dans publish\ : dotnet publish src/BarcodeBoard -c Release -o publish)

; Nom technique : dossier d'installation (le nom affiché est le message AppName de chaque langue)
#define AppName "Barcode Board"
#define AppExe "BarcodeBoard.exe"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

; Page de licence : LICENSE.md mis en forme (RTF), plus lisible que le Markdown brut dans la fenêtre de l'installeur
#define LicenseRtf SourcePath + "obj\License.rtf"
#if Exec(GetEnv("SystemRoot") + "\System32\WindowsPowerShell\v1.0\powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File """ + SourcePath + "license-rtf.ps1"" """ + SourcePath + "..\LICENSE.md"" """ + LicenseRtf + """", SourcePath, 1, SW_HIDE) != 0
  #error LICENSE.md could not be converted for the license page
#endif

[Setup]
AppId={{1B6750FA-7100-4E8E-A760-6DB9EBEC5C8B}
AppName={cm:AppName}
AppVersion={#AppVersion}
AppVerName={cm:AppName} {#AppVersion}
AppPublisher=ArbitroDev
AppPublisherURL=https://github.com/ArbitroDev/BarcodeBoard
AppSupportURL=https://github.com/ArbitroDev/BarcodeBoard/issues
AppUpdatesURL=https://github.com/ArbitroDev/BarcodeBoard/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={cm:AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\artifacts
OutputBaseFilename=BarcodeBoard-Setup-{#AppVersion}
SetupIconFile=..\src\BarcodeBoard\Assets\BarcodeBoard.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={cm:AppName}
LicenseFile={#LicenseRtf}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ChangesAssociations=yes
CloseApplications=yes

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
french.AppName=Tableau de code-barres
english.AppName=Barcode Board
french.AssociateCodes=Ouvrir les fichiers .codes avec Tableau de code-barres
english.AssociateCodes=Open .codes files with Barcode Board
french.CodesFileType=Onglet de codes (Tableau de code-barres)
english.CodesFileType=Barcode Board tab
french.NewerInstalled=La version %1 de Tableau de code-barres, plus récente que celle-ci (%2), est déjà installée.%n%nPour revenir à une version antérieure, désinstallez d'abord la version installée.
english.NewerInstalled=Barcode Board %1, newer than this version (%2), is already installed.%n%nTo go back to an older version, uninstall the installed version first.

[Tasks]
Name: "associate"; Description: "{cm:AssociateCodes}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{cm:AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{cm:AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Association des fichiers .codes (dans HKCU pour une installation par utilisateur)
Root: HKA; Subkey: "Software\Classes\.codes"; ValueType: string; ValueName: ""; ValueData: "BarcodeBoard.codes"; Flags: uninsdeletevalue; Tasks: associate
Root: HKA; Subkey: "Software\Classes\BarcodeBoard.codes"; ValueType: string; ValueName: ""; ValueData: "{cm:CodesFileType}"; Flags: uninsdeletekey; Tasks: associate
Root: HKA; Subkey: "Software\Classes\BarcodeBoard.codes\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe},0"; Tasks: associate
Root: HKA; Subkey: "Software\Classes\BarcodeBoard.codes\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: associate

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{cm:AppName}}"; Flags: nowait postinstall skipifsilent

[Code]
// Mise à jour : même AppId, donc même dossier, mêmes tâches, et l'application ouverte est fermée puis relancée.
// Une version plus récente déjà installée n'est en revanche jamais remplacée par une plus ancienne.
const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{1B6750FA-7100-4E8E-A760-6DB9EBEC5C8B}_is1';

// Compare deux versions « 1.2.3 » nombre par nombre : > 0 si A est plus récente que B.
function CompareVersions(A, B: String): Integer;
var
  PartA, PartB: Integer;
begin
  Result := 0;
  while (Result = 0) and ((A <> '') or (B <> '')) do
  begin
    if Pos('.', A) > 0 then begin PartA := StrToIntDef(Copy(A, 1, Pos('.', A) - 1), 0); Delete(A, 1, Pos('.', A)); end
    else begin PartA := StrToIntDef(A, 0); A := ''; end;
    if Pos('.', B) > 0 then begin PartB := StrToIntDef(Copy(B, 1, Pos('.', B) - 1), 0); Delete(B, 1, Pos('.', B)); end
    else begin PartB := StrToIntDef(B, 0); B := ''; end;
    if PartA > PartB then Result := 1
    else if PartA < PartB then Result := -1;
  end;
end;

// Version installée la plus récente, pour l'utilisateur ou pour tout l'ordinateur ; vide si aucune.
function InstalledVersion(): String;
var
  Version: String;
begin
  Result := '';
  if RegQueryStringValue(HKCU, UninstallKey, 'DisplayVersion', Version) and (CompareVersions(Version, Result) > 0) then Result := Version;
  if RegQueryStringValue(HKLM32, UninstallKey, 'DisplayVersion', Version) and (CompareVersions(Version, Result) > 0) then Result := Version;
  if IsWin64 and RegQueryStringValue(HKLM64, UninstallKey, 'DisplayVersion', Version) and (CompareVersions(Version, Result) > 0) then Result := Version;
end;

function InitializeSetup(): Boolean;
var
  Installed: String;
begin
  Result := True;
  Installed := InstalledVersion();
  if (Installed <> '') and (CompareVersions(Installed, '{#AppVersion}') > 0) then
  begin
    SuppressibleMsgBox(FmtMessage(CustomMessage('NewerInstalled'), [Installed, '{#AppVersion}']), mbError, MB_OK, IDOK);
    Result := False;
  end;
end;
