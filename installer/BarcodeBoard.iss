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
LicenseFile=..\LICENSE.md
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
