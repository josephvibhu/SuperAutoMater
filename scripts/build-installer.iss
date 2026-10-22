; Velox Platform Suite — Professional Inno Setup Installer Script
; Abzard Inc. (c) 2026. All rights reserved.

#define MyAppName "Velox Platform Suite"
#define MyAppVersion "1.6.10"
#define MyAppPublisher "Abzard Inc."
#define MyAppURL "https://13.206.106.146"
#define MyOutputDir "..\Releases\Installer"

[Setup]
AppId={{D9A4C62B-4A1C-46F2-8703-9A25E815F83C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/api/v1/update/check
DefaultDirName={localappdata}\Velox
DefaultGroupName=Velox Suite
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#MyOutputDir}
OutputBaseFilename=VeloxSetup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\VeloxBench\app.ico
UninstallDisplayIcon={app}\VeloxBench.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Primary binaries and configs
Source: "..\Releases\VeloxBench\VeloxBench.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Releases\VeloxBench\config.json"; DestDir: "{app}"; Flags: onlyifdoesntexist
Source: "..\VeloxBench\lib\*"; DestDir: "{app}\lib"; Flags: ignoreversion recursesubdirs createallsubdirs; Permissions: users-full
Source: "..\VeloxBench\app.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\VeloxBench — Laptop Diagnostics"; Filename: "{app}\VeloxBench.exe"; IconFilename: "{app}\app.ico"
Name: "{group}\Velox Link — Mobile Companion"; Filename: "{#MyAppURL}/link"; IconFilename: "{app}\app.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\VeloxBench"; Filename: "{app}\VeloxBench.exe"; IconFilename: "{app}\app.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\VeloxBench.exe"; Description: "{cm:LaunchProgram,VeloxBench}"; Flags: nowait postinstall skipifsilent
