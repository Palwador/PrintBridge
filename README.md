# PrintBridge

A SOLIDWORKS add-in for a faster prototype loop:

1. Click a dedicated SOLIDWORKS command.
2. Pick a solid body from the active part or from a resolved component in the active assembly.
3. Export it as `.STL` or `.STEP`; STEP exports are forced to AP214.
4. Save it with an editable file name and an incrementing suffix like `Bracket_Left_V001.STL`.
5. Launch the slicer program you choose with the exported file path. Common slicers are detected automatically.

## Project Shape

- `src/SwPrototypeExporter.csproj` - Visual Studio C# class library project.
- `src/SwAddin.cs` - COM-visible SOLIDWORKS add-in entry point and toolbar/menu command.
- `src/ExportWorkflow.cs` - Body discovery, versioned filename generation, export, and slicer launch.
- `src/ExportDialog.cs` - Small Windows Forms dialog for body, format, folder, and slicer choices.
- `src/SlicerDiscovery.cs` - Finds installed slicers from common install folders and Windows uninstall registry entries.
- `src/SlicerSettings.cs` - Saves your last folder/slicer choices under `%APPDATA%\SwPrototypeExporter`.
- `install/Register-Addin.ps1` - Registers the compiled DLL with COM/SOLIDWORKS.
- `install/Unregister-Addin.ps1` - Unregisters the add-in.
- `install/Package-Installer.ps1` - Builds a Release DLL and packages a Windows installer.
- `installer/PrintBridge.iss` - Inno Setup definition for the distributable installer.

## Requirements

- SOLIDWORKS installed locally.
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

## Package an Installer

Install Inno Setup 6 on the packaging machine, close SOLIDWORKS, then run:

```powershell
.\install\Package-Installer.ps1
```

The installer is written to:

```text
dist\PrintBridgeSetup-0.1.1.exe
```

Upload that `.exe` to a GitHub Release. Users should download the installer, run it, open SOLIDWORKS, and enable `PrintBridge` in:

```text
Tools > Add-Ins
```

The installer copies the add-in to `Program Files`, registers it as a 64-bit COM/SOLIDWORKS add-in, and adds an uninstaller under Windows Apps & Features. It does not force the add-in to start automatically; users can check the `Start Up` box in SOLIDWORKS Add-Ins if they want that.

## Use

1. Open a part document.
2. Click `PrintBridge` from the add-in toolbar/menu.
3. Check one or more bodies, choose the output format, destination folder, file name, and slicer app.
4. Click the green checkmark.

The add-in suggests the next available versioned filename in the destination folder. You can edit it before exporting. For example:

```text
MyPart_MainBody_V001.STL
MyPart_MainBody_V002.STL
MyPart_MainBody_V003.STL
```

The slicer field auto-detects common slicers such as PrusaSlicer, Bambu Studio, OrcaSlicer, SuperSlicer, and Cura. You can still browse to any `.exe` manually. Most slicers can be launched with a model file path as a command-line argument. If your slicer needs different command-line arguments, adjust `LaunchSlicer` in `src/ExportWorkflow.cs`.

For STL exports, the add-in copies the selected body into a temporary hidden part and asks SOLIDWORKS to export that temporary part. That avoids empty selected-body STL exports in SOLIDWORKS. STEP exports use AP214 by temporarily setting `swUserPreferenceIntegerValue_e.swStepAP` to `214` around the export call.

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
