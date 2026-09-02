#ifndef AppVersion
#define AppVersion "0.1.0"
#endif

#define AddinGuid "{{040C231A-2571-4FFC-894D-8D01C2530606}}"

[Setup]
AppId={{B97F4FC7-18BD-4D03-A390-05335D673DB1}
AppName=Export to 3D-printer
AppVersion={#AppVersion}
AppPublisher=
AppPublisherURL=
AppSupportURL=
AppUpdatesURL=
DefaultDirName={autopf}\Export to 3D-printer
DefaultGroupName=Export to 3D-printer
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=ExportTo3DPrinterSetup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
SetupLogging=yes
UninstallDisplayName=Export to 3D-printer SOLIDWORKS Add-in
UninstallDisplayIcon={app}\SwPrototypeExporter.dll

[Files]
Source: "..\src\bin\x64\Release\SwPrototypeExporter.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\bin\x64\Release\SolidWorks.Interop.sldworks.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\bin\x64\Release\SolidWorks.Interop.swconst.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\bin\x64\Release\SolidWorks.Interop.swpublished.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\bin\x64\Release\assets\*"; DestDir: "{app}\assets"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
Root: HKLM64; Subkey: "SOFTWARE\SOLIDWORKS\Addins\{#AddinGuid}"; ValueType: dword; ValueName: ""; ValueData: "1"; Flags: uninsdeletekey
Root: HKLM64; Subkey: "SOFTWARE\SOLIDWORKS\Addins\{#AddinGuid}"; ValueType: string; ValueName: "Title"; ValueData: "Export to 3D-printer"
Root: HKLM64; Subkey: "SOFTWARE\SOLIDWORKS\Addins\{#AddinGuid}"; ValueType: string; ValueName: "Description"; ValueData: "Exports selected bodies as STL or STEP and opens them in your slicer."
Root: HKCU; Subkey: "Software\SOLIDWORKS\AddInsStartup\{#AddinGuid}"; ValueType: dword; ValueName: ""; ValueData: "0"; Flags: uninsdeletekey

[Run]
Filename: "{win}\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"; Parameters: """{app}\SwPrototypeExporter.dll"" /codebase"; Flags: runhidden waituntilterminated; StatusMsg: "Registering the SOLIDWORKS add-in..."

[UninstallRun]
Filename: "{win}\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"; Parameters: """{app}\SwPrototypeExporter.dll"" /unregister"; Flags: runhidden waituntilterminated
