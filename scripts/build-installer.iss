; Velox Platform Suite — Professional Inno Setup Installer Script
; Abzard Inc. (c) 2026. All rights reserved.

#define MyAppName "Velox Hub"
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
DefaultDirName={localappdata}\Programs\VeloxHub
DefaultGroupName=Velox Suite
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#MyOutputDir}
OutputBaseFilename=VeloxHubSetup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\VeloxBench\app.ico
UninstallDisplayIcon={app}\VeloxHub.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Primary binaries and configs
Source: "..\Releases\VeloxHub\VeloxHub.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\VeloxBench\app.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Velox Hub"; Filename: "{app}\VeloxHub.exe"; IconFilename: "{app}\app.ico"
Name: "{group}\Velox Link — Mobile Companion"; Filename: "{#MyAppURL}/link"; IconFilename: "{app}\app.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Velox Hub"; Filename: "{app}\VeloxHub.exe"; IconFilename: "{app}\app.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\VeloxHub.exe"; Description: "{cm:LaunchProgram,Velox Hub}"; Flags: nowait postinstall skipifsilent
