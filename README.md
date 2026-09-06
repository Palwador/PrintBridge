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
- `src/ExportWorkflow.cs` - Body discovery, versioned filename generation, export, and slicer launch.
- `src/ExportDialog.cs` - Small Windows Forms dialog for body, format, folder, and slicer choices.
- `src/SlicerDiscovery.cs` - Finds installed slicers from common install folders and Windows uninstall registry entries.
- `src\AppPaths.cs` - Centralizes runtime paths under `%APPDATA%\PrintBridge`.
- `src/SlicerSettings.cs` - Saves your last folder/slicer choices under `%APPDATA%\PrintBridge\TemporaryExports`.
- `install/Register-Addin.ps1` - Registers the compiled DLL with COM/SOLIDWORKS.
- `install/Unregister-Addin.ps1` - Unregisters the add-in.
- `install/Package-Installer.ps1` - Builds a Release DLL and packages a Windows installer.
- `installer/PrintBridge.iss` - Inno Setup definition for the distributable installer.

## Requirements

- Windows required.
- SOLIDWORKS installed locally. This project is set up for SOLIDWORKS 2025 x64 on this workstation.
- Visual Studio with .NET Framework 4.8 targeting support.
- Administrator PowerShell for add-in registration, because SOLIDWORKS add-ins are registered under HKLM.

The project references the SOLIDWORKS interop DLLs from:

```text
C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS
```

That path exists on this machine.

## Build

Open `src/SwPrototypeExporter.csproj` in Visual Studio and build `Release | x64`.

Or close SOLIDWORKS and run:

```powershell
.\install\Build-Addin.ps1 -Configuration Release
```

The expected output is:

```text
src\bin\x64\Release\SwPrototypeExporter.dll
```

## Register

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
dist\PrintBridgeSetup-0.1.3.exe
```

Upload that `.exe` to a GitHub Release. Users should download the installer, run it, open SOLIDWORKS, and enable `PrintBridge` in:

```text
Tools > Add-Ins
```

The installer copies the add-in to `Program Files`, registers it as a 64-bit COM/SOLIDWORKS add-in, and adds an uninstaller under Windows Apps & Features. It does not force the add-in to start automatically; users can check the `Start Up` box in SOLIDWORKS Add-Ins if they want that. Settings, logs, generated toolbar bitmaps, and temporary export files are written under `%APPDATA%\PrintBridge`, not beside the installed DLL.

## Current Scope

This starter version supports solid bodies in active part documents and visible resolved component bodies in active assembly documents. The next useful upgrades would be:

- Add optional assembly-position-aware exports.
- Add per-format options, especially STL resolution.
- Add a persistent "favorite export folder per project" option.
- Add an icon strip for a nicer SOLIDWORKS toolbar button.
- Add a one-click mode that skips the dialog when a body is already selected.

## Notes From SOLIDWORKS API Docs

SOLIDWORKS add-ins implement `ISwAddin`; SOLIDWORKS calls `ConnectToSW` when loading the add-in and `DisconnectFromSW` when unloading it. The add-in is registered as a COM server and added to SOLIDWORKS registry keys.

SOLIDWORKS `ICommandManager`/`ICommandGroup` is the right API for creating native toolbar and menu commands.

For exporting STEP, SOLIDWORKS `IModelDocExtension.SaveAs`/`SaveAs2` exports the active model, but if bodies or faces are selected, it exports only the selected items. This scaffold relies on that behavior by selecting the chosen body immediately before STEP export. For STL, the add-in uses a temporary part containing only the selected body.
