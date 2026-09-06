#ifndef AppVersion
#define AppVersion "0.1.0"
#endif

#define AddinGuid "{{040C231A-2571-4FFC-894D-8D01C2530606}}"

[Setup]
AppId={{B97F4FC7-18BD-4D03-A390-05335D673DB1}
AppName=PrintBridge
AppVersion={#AppVersion}
AppPublisher=
AppPublisherURL=
AppSupportURL=
AppUpdatesURL=
DefaultDirName={autopf}\PrintBridge
DefaultGroupName=PrintBridge
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=PrintBridgeSetup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
SetupLogging=yes
UninstallDisplayName=PrintBridge SOLIDWORKS Add-in
UninstallDisplayIcon={app}\SwPrototypeExporter.dll

[Files]
Source: "..\src\bin\x64\Release\SwPrototypeExporter.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\bin\x64\Release\SolidWorks.Interop.sldworks.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\bin\x64\Release\SolidWorks.Interop.swconst.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\bin\x64\Release\SolidWorks.Interop.swpublished.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\bin\x64\Release\assets\*"; DestDir: "{app}\assets"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
Root: HKLM64; Subkey: "SOFTWARE\SOLIDWORKS\Addins\{#AddinGuid}"; ValueType: dword; ValueName: ""; ValueData: "1"; Flags: uninsdeletekey
Root: HKLM64; Subkey: "SOFTWARE\SOLIDWORKS\Addins\{#AddinGuid}"; ValueType: string; ValueName: "Title"; ValueData: "PrintBridge"
Root: HKLM64; Subkey: "SOFTWARE\SOLIDWORKS\Addins\{#AddinGuid}"; ValueType: string; ValueName: "Description"; ValueData: "Exports selected SOLIDWORKS bodies as STL or STEP and opens them in your slicer."

[Run]
Filename: "{win}\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"; Parameters: """{app}\SwPrototypeExporter.dll"" /codebase"; Flags: runhidden waituntilterminated; StatusMsg: "Registering the SOLIDWORKS add-in..."

[UninstallRun]
Filename: "{win}\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe"; Parameters: """{app}\SwPrototypeExporter.dll"" /unregister"; Flags: runhidden waituntilterminated; RunOnceId: "UnregisterPrintBridge"

[Code]
function IsSolidWorksRunning(): Boolean;
var
  ResultCode: Integer;
begin
  Result :=
    Exec(
      ExpandConstant('{cmd}'),
      '/C tasklist /FI "IMAGENAME eq SLDWORKS.exe" | find /I "SLDWORKS.exe" > nul',
      '',
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode) and (ResultCode = 0);
end;

procedure TryUnregisterExistingAddin();
var
  ResultCode: Integer;
  DllPath: String;
begin
  DllPath := ExpandConstant('{app}\SwPrototypeExporter.dll');
  if FileExists(DllPath) then
  begin
    Exec(
      ExpandConstant('{win}\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe'),
      '"' + DllPath + '" /unregister',
      '',
      SW_HIDE,
      ewWaitUntilTerminated,
      ResultCode);
  end;
end;

procedure DeleteOldProgramFiles();
begin
  DeleteFile(ExpandConstant('{app}\SwPrototypeExporter.dll'));
  DeleteFile(ExpandConstant('{app}\SolidWorks.Interop.sldworks.dll'));
  DeleteFile(ExpandConstant('{app}\SolidWorks.Interop.swconst.dll'));
  DeleteFile(ExpandConstant('{app}\SolidWorks.Interop.swpublished.dll'));
  DelTree(ExpandConstant('{app}\assets'), True, True, True);
  DelTree(ExpandConstant('{app}\icons'), True, True, True);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';

  if IsSolidWorksRunning() then
  begin
    Result := 'Close SOLIDWORKS before installing or updating PrintBridge. SOLIDWORKS is currently holding the add-in files open.';
    Exit;
  end;

  TryUnregisterExistingAddin();
  DeleteOldProgramFiles();
end;
