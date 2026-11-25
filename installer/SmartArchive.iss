#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

[Setup]
AppId={{B3F1C7D2-5A4E-4F59-9C61-7E2A9D0C41AB}
AppName=Smart Archive
AppVersion={#MyAppVersion}
AppPublisher=Smart Archive
DefaultDirName={autopf}\Smart Archive
DefaultGroupName=Smart Archive
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist
OutputBaseFilename=SmartArchive-Setup-{#MyAppVersion}
SetupIconFile=..\assets\icon.ico
UninstallDisplayIcon={app}\SmartArchive.exe
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
DisableProgramGroupPage=yes

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "shellmenu"; Description: "Agregar opciones de Smart Archive al menú contextual del Explorador (clic derecho)"
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Smart Archive"; Filename: "{app}\SmartArchive.exe"
Name: "{autodesktop}\Smart Archive"; Filename: "{app}\SmartArchive.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\SmartArchive.exe"; Parameters: "--register-shell"; Flags: runhidden; Tasks: shellmenu
Filename: "{app}\SmartArchive.exe"; Description: "Abrir Smart Archive"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\SmartArchive.exe"; Parameters: "--unregister-shell"; Flags: runhidden; RunOnceId: "UnregShell"
