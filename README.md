# PrintBridge

A SOLIDWORKS add-in for a faster prototype loop:

1. Click a dedicated SOLIDWORKS command.
2. Pick a solid body from the active part or from a resolved component in the active assembly.
3. Export it as `.STL` or `.STEP`; STEP exports are forced to AP214.
4. Save it with an editable file name and an incrementing suffix like `Bracket_Left_V001.STL`.
5. Launch the slicer program you choose with the exported file path. Common slicers are detected automatically.

## Using PrintBridge

PrintBridge adds an `Export to 3D-printer` command to SOLIDWORKS. When opened, it shows a left-side panel where you choose what to export and where the exported file should go.

### Bodies

The body list shows the solid bodies available in the active part or assembly. Check one or more bodies to include them in the export. You can also select bodies directly in the SOLIDWORKS model; PrintBridge will update the checked list to match.

Selected bodies are highlighted in the model so you can confirm what will be exported before pressing `Export`.

### Output

Choose whether to export as `STL` or `STEP AP214`.

The folder field controls where normal exports are saved. Use `Browse folder` to choose a different destination.

The file name field controls the exported file name. PrintBridge automatically uses version-style suffixes such as `_V001`, `_V002`, and `_V003` so repeated exports do not overwrite earlier prototypes.

Enable `Export selected bodies as separate files` when you want each selected body written as its own export file. Leave it disabled when you want the selected bodies combined into one exported file.

Enable `Use temporary export file` when you want PrintBridge to send the file to your slicer without keeping extra export files next to your SOLIDWORKS model. Temporary exports are stored under `%APPDATA%\PrintBridge\TemporaryExports`; PrintBridge keeps the five newest temporary export files and removes older ones automatically.

### Slicer

The slicer selector shows slicer programs PrintBridge was able to find automatically. Use `Browse slicer` if your slicer is not listed or if you want to choose a specific executable manually.

Enable `Open in slicer after export` to launch the selected slicer with the exported file. Disable it if you only want to save the STL or STEP file.

## Uninstall PrintBridge

To completely remove the installed version of PrintBridge, use Windows' uninstall tool.

On Windows 11, go to:

```text
Settings > Apps > Installed apps
```
On Windows 10, go to:
```
Control Panel > Programs > Programs and Features
```

Look for:

```
PrintBridge SOLIDWORKS Add-in
```
Uninstalling removes the installed add-in files from C:\Program Files\PrintBridge and unregisters PrintBridge from SOLIDWORKS.
Disabling PrintBridge from Tools.

Add-Ins inside SOLIDWORKS only stops the add-in from loading. It does not remove the installed files or Windows/SOLIDWORKS registration.
User settings, logs, and temporary export files are stored under %APPDATA%\PrintBridge\TemporaryExports and are left in place when PrintBridge is uninstalled.

## Project Shape

- `src/SwPrototypeExporter.csproj` - Visual Studio C# class library project.
- `src/SwAddin.cs` - COM-visible SOLIDWORKS add-in entry point and toolbar/menu command.
- `src/ExportPropertyManagerPage.cs` - SOLIDWORKS left-panel interface for selecting bodies, output settings, and slicer options.
- `src/ExportWorkflow.cs` - Body discovery, versioned filename generation, export, and slicer launch.
- `src/ExportDialog.cs` - Fallback Windows Forms dialog used if the SOLIDWORKS left-panel interface cannot be opened.
- `src/SlicerDiscovery.cs` - Finds installed slicers from common install folders and Windows uninstall registry entries.
- `src\AppPaths.cs` - Centralizes runtime paths under `%APPDATA%\PrintBridge`.
- `src\SlicerSettings.cs` - Saves your last folder/slicer choices under `%APPDATA%\PrintBridge`.
- `install/Register-Addin.ps1` - Registers the compiled DLL with COM/SOLIDWORKS.
- `install/Unregister-Addin.ps1` - Unregisters the add-in.
- `install/Package-Installer.ps1` - Builds a Release DLL and packages a Windows installer.
- `installer/PrintBridge.iss` - Inno Setup definition for the distributable installer.

## Requirements

For users installing PrintBridge:

- Windows required. PrintBridge is a Windows/SOLIDWORKS add-in.
- SOLIDWORKS installed locally. PrintBridge was developed and tested with SOLIDWORKS 2025 x64.
- Administrator permission may be required when running the installer, because SOLIDWORKS add-ins are registered system-wide.

For developers building PrintBridge:

- Visual Studio with .NET Framework 4.8 targeting support.
- SOLIDWORKS 2025 x64 installed locally, including the SOLIDWORKS interop DLLs.

The project references the SOLIDWORKS interop DLLs from:

```text
C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS
```

If SOLIDWORKS is installed somewhere else, update the reference paths in the project file.

## Build

Open `src/SwPrototypeExporter.csproj` in Visual Studio and build `Release | x64`.

SOLIDWORKS should be closed before building, because it can hold the add-in DLL open.

Or close SOLIDWORKS and run:

```powershell
.\install\Build-Addin.ps1 -Configuration Release

```
The expected output is:

```text
src\bin\x64\Release\SwPrototypeExporter.dll
```

## Register Manually

This step is only needed when developing or testing PrintBridge without the installer. Normal users should install PrintBridge with the `.exe` installer instead.

Open PowerShell as Administrator from the repository root and run:

```powershell
.\install\Register-Addin.ps1 -Configuration Release
```

Then open SOLIDWORKS and enable `PrintBridge` in:

```text
Tools > Add-Ins
```

That registration command does not force the add-in to start automatically. To register it and also check the SOLIDWORKS `Start Up` flag for the current user, run:

```powershell
.\install\Register-Addin.ps1 -Configuration Release -StartOnOpen
```

## Package an Installer

Install Inno Setup 6 on the packaging machine, close SOLIDWORKS, then run:

```powershell
.\install\Package-Installer.ps1
```

The installer is written to:

```text
dist\PrintBridgeSetup-0.1.4.exe
```

Upload that .exe to a GitHub Release. Users should download the installer, close SOLIDWORKS, run the installer, open SOLIDWORKS, and enable PrintBridge in:

```text
Tools > Add-Ins
```

The installer copies the add-in to C:\Program Files\PrintBridge, registers it as a 64-bit COM/SOLIDWORKS add-in, and adds an uninstaller under Windows Apps & Features.
If PrintBridge is already installed, the installer also acts as an updater: it checks that SOLIDWORKS is closed, unregisters the existing installed DLL, removes old installed program files, installs the new files, and registers the new DLL.
The installer does not force the add-in to start automatically. Users can check the Start Up box in SOLIDWORKS Add-Ins if they want PrintBridge to load whenever SOLIDWORKS opens.
Settings, logs, and generated toolbar/help icons are written under %APPDATA%\PrintBridge. Temporary export files are written under %APPDATA%\PrintBridge\TemporaryExports.

## Current Scope

PrintBridge currently supports solid bodies in active part documents and visible resolved component bodies in active assembly documents.

Possible future improvements include:

- Improve assembly-position handling for more complex assembly export cases.
- Add per-format export options, especially STL resolution.
- Add a persistent favorite export folder per project.
- Add a one-click mode that skips the panel when a body is already selected.

## Notes From SOLIDWORKS API Docs

SOLIDWORKS add-ins implement `ISwAddin`; SOLIDWORKS calls `ConnectToSW` when loading the add-in and `DisconnectFromSW` when unloading it. The add-in is registered as a COM server and added to SOLIDWORKS registry keys.

SOLIDWORKS `ICommandManager`/`ICommandGroup` is the right API for creating native toolbar and menu commands.

For STEP exports, PrintBridge uses SOLIDWORKS `IModelDocExtension.SaveAs`/`SaveAs2`. STEP exports are forced to AP214 by temporarily setting `swUserPreferenceIntegerValue_e.swStepAP` to `214` around the export call.

For STL exports from part documents, PrintBridge writes a binary STL from selected-body tessellation so selected-body exports do not accidentally include the entire part. Some non-part export paths use a temporary part containing only the selected body or bodies.
