using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace SwPrototypeExporter
{
    internal enum ExportFormat
    {
        Stl,
        Step
    }

    internal sealed class ExportWorkflow
    {
        internal const string DialogTitle = "PrintBridge";
        private readonly ISldWorks _swApp;

        public ExportWorkflow(ISldWorks swApp)
        {
            _swApp = swApp;
        }

        public void Run()
        {
            ExportContext context = CreateContext();
            if (context == null)
            {
                return;
            }

            var page = new ExportPropertyManagerPage(this, context);
            try
            {
                page.Show();
            }
            catch (Exception ex)
            {
                Log("PropertyManager page failed. Falling back to dialog. " + ex);
                MessageBox.Show(
                    "The SOLIDWORKS left-panel page could not be opened, so the standard exporter dialog will be used instead.\r\n\r\n" + ex.Message,
                    DialogTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                RunDialogFallback(context);
            }
        }

        internal ISldWorks SwApp
        {
            get { return _swApp; }
        }

        internal ExportContext CreateContext()
        {
            ModelDoc2 model = _swApp == null ? null : _swApp.ActiveDoc as ModelDoc2;
            if (model == null)
            {
                MessageBox.Show("Open a part or assembly document first.", DialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }

            int documentType = model.GetType();
            if (documentType != (int)swDocumentTypes_e.swDocPART && documentType != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                MessageBox.Show("This exporter supports part and assembly documents.", DialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }

            List<BodyExportItem> bodies = GetBodyExportItems(model).ToList();
            if (bodies.Count == 0)
            {
                MessageBox.Show("No visible solid bodies were found in the active document.", DialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }

            SlicerSettings settings = SlicerSettings.Load();
            BodyExportItem selectedBody = GetSelectedBody(model, bodies);
            string initialFolder = ResolveInitialFolder(settings.ExportDirectory, model.GetPathName());

            return new ExportContext(model, bodies, selectedBody, initialFolder, settings);
        }

        internal void RunDialogFallback()
        {
            ExportContext context = CreateContext();
            if (context == null)
            {
                return;
            }

            RunDialogFallback(context);
        }

        private void RunDialogFallback(ExportContext context)
        {
            using (var dialog = new ExportDialog(
                context.Bodies,
                context.InitiallySelectedBody,
                context.InitialFolder,
                context.Model.GetPathName(),
                context.Settings))
            {
                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                Export(new ExportRequest(
                    context.Model,
                    new[] { dialog.SelectedItem },
                    dialog.DestinationDirectory,
                    dialog.FileName,
                    dialog.Format,
                    dialog.SlicerExecutable,
                    dialog.LaunchSlicer,
                    false,
                    false));
            }
        }

        internal void Export(ExportRequest request)
        {
            if (request == null || request.Model == null || request.SelectedItems == null || request.SelectedItems.Count == 0)
            {
                MessageBox.Show("Choose at least one body to export.", DialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SlicerSettings settings = SlicerSettings.Load();
            settings.ExportDirectory = request.DestinationDirectory;
            settings.SlicerPath = request.SlicerExecutable;
            settings.LaunchSlicer = request.LaunchSlicer;
            settings.LastFormat = request.Format.ToString();
            settings.UseTemporaryFile = request.UseTemporaryFile;
            settings.Save();

            if (request.UseTemporaryFile)
            {
                CleanOldTemporaryExports();
            }

            List<string> exportedFiles = request.ExportSeparateFiles && request.SelectedItems.Count > 1
                ? ExportSeparateFiles(request)
                : ExportOneFile(request);

            if (exportedFiles.Count == 0)
            {
                return;
            }

            if (request.LaunchSlicer && !string.IsNullOrWhiteSpace(request.SlicerExecutable))
            {
                foreach (string exportedFile in exportedFiles)
                {
                    WaitForExportedFile(exportedFile);
                }

                LaunchSlicer(request.SlicerExecutable, exportedFiles);
            }

            string message = exportedFiles.Count == 1
                ? "Exported:\r\n" + exportedFiles[0]
                : "Exported " + exportedFiles.Count + " files:\r\n" + string.Join("\r\n", exportedFiles.ToArray());
            MessageBox.Show(message, DialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private List<string> ExportOneFile(ExportRequest request)
        {
            string exportPath = request.UseTemporaryFile
                ? CreateTemporaryExportPath(request.FileName, request.Format)
                : CreateExportPath(request.DestinationDirectory, request.FileName, request.Format);

            if (!request.UseTemporaryFile && !ConfirmOverwrite(new[] { exportPath }))
            {
                return new List<string>();
            }

            if (request.SelectedItems.Count == 1)
            {
                ExportSelectedBody(request.Model, request.SelectedItems[0], exportPath, request.Format);
            }
            else
            {
                ExportBodiesThroughTemporaryPart(request.Model, request.SelectedItems.Select(item => item.Body), exportPath, request.Format);
            }

            return new List<string> { exportPath };
        }

        private List<string> ExportSeparateFiles(ExportRequest request)
        {
            var exportPaths = new List<string>();
            string destinationDirectory = request.UseTemporaryFile
                ? GetTemporaryExportDirectory()
                : request.DestinationDirectory;
            string baseStem = StripVersionSuffix(SanitizeFileName(Path.GetFileNameWithoutExtension(request.FileName)));

            foreach (BodyExportItem item in request.SelectedItems)
            {
                string fileName = CreateNextVersionFileName(
                    destinationDirectory,
                    baseStem + "_" + item.FileStemName,
                    request.Format);
                exportPaths.Add(Path.Combine(destinationDirectory, fileName));
            }

            if (!request.UseTemporaryFile && !ConfirmOverwrite(exportPaths))
            {
                return new List<string>();
            }

            for (int i = 0; i < request.SelectedItems.Count; i++)
            {
                ExportSelectedBody(request.Model, request.SelectedItems[i], exportPaths[i], request.Format);
            }

            return exportPaths;
        }

        private static bool ConfirmOverwrite(IEnumerable<string> exportPaths)
        {
            List<string> existingFiles = exportPaths.Where(File.Exists).ToList();
            if (existingFiles.Count == 0)
            {
                return true;
            }

            string message = existingFiles.Count == 1
                ? "This file already exists:\r\n" + existingFiles[0] + "\r\n\r\nOverwrite it?"
                : "These files already exist:\r\n" + string.Join("\r\n", existingFiles.ToArray()) + "\r\n\r\nOverwrite them?";

            DialogResult overwrite = MessageBox.Show(
                message,
                DialogTitle,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            return overwrite == DialogResult.Yes;
        }

        internal static IEnumerable<BodyExportItem> GetBodyExportItems(ModelDoc2 model)
        {
            if (model.GetType() == (int)swDocumentTypes_e.swDocPART)
            {
                foreach (Body2 body in GetPartSolidBodies(model))
                {
                    string bodyName = GetBodyName(body);
                    string modelName = string.IsNullOrWhiteSpace(model.GetPathName())
                        ? "UnsavedPart"
                        : Path.GetFileNameWithoutExtension(model.GetPathName());

                    yield return new BodyExportItem(
                        body,
                        null,
                        bodyName,
                        modelName + "_" + bodyName,
                        false);
                }

                yield break;
            }

            var assembly = model as AssemblyDoc;
            if (assembly == null)
            {
                yield break;
            }

            try
            {
                assembly.ResolveAllLightWeightComponents(false);
            }
            catch
            {
            }

            object[] components = assembly.GetComponents(false) as object[];
            if (components == null)
            {
                yield break;
            }

            foreach (object componentObject in components)
            {
                var component = componentObject as Component2;
                if (component == null || IsComponentUnavailable(component))
                {
                    continue;
                }

                object bodiesInfo;
                object[] bodies = null;
                try
                {
                    bodies = component.GetBodies3((int)swBodyType_e.swSolidBody, out bodiesInfo) as object[];
                }
                catch
                {
                    bodies = component.GetBodies2((int)swBodyType_e.swSolidBody) as object[];
                }

                if (bodies == null)
                {
                    continue;
                }

                foreach (object bodyObject in bodies)
                {
                    var body = bodyObject as Body2;
                    if (body == null)
                    {
                        continue;
                    }

                    string componentName = GetComponentName(component);
                    string bodyName = GetBodyName(body);
                    string displayName = componentName + " - " + bodyName;

                    yield return new BodyExportItem(
                        body,
                        component,
                        displayName,
                        componentName + "_" + bodyName,
                        true);
                }
            }
        }

        private static IEnumerable<Body2> GetPartSolidBodies(ModelDoc2 model)
        {
            var part = model as PartDoc;
            if (part == null)
            {
                yield break;
            }

            object result = part.GetBodies2((int)swBodyType_e.swSolidBody, true);
            object[] bodies = result as object[];
            if (bodies == null)
            {
                yield break;
            }

            foreach (object bodyObject in bodies)
            {
                var body = bodyObject as Body2;
                if (body != null)
                {
                    yield return body;
                }
            }
        }

        private static BodyExportItem GetSelectedBody(ModelDoc2 model, IReadOnlyList<BodyExportItem> items)
        {
            SelectionMgr selection = model.SelectionManager as SelectionMgr;
            if (selection == null || selection.GetSelectedObjectCount2(-1) < 1)
            {
                return null;
            }

            object selected = selection.GetSelectedObject6(1, -1);
            var selectedBody = selected as Body2;
            Component2 selectedComponent = null;

            if (model.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                selectedComponent = selected as Component2;
                if (selectedComponent == null)
                {
                    selectedComponent = selection.GetSelectedObjectsComponent3(1, -1);
                }
            }

            foreach (BodyExportItem item in items)
            {
                if (selectedComponent != null
                    && item.Component != null
                    && string.Equals(GetComponentName(item.Component), GetComponentName(selectedComponent), StringComparison.OrdinalIgnoreCase))
                {
                    if (selectedBody == null || string.Equals(GetBodyName(item.Body), GetBodyName(selectedBody), StringComparison.OrdinalIgnoreCase))
                    {
                        return item;
                    }
                }

                if (selectedBody != null
                    && string.Equals(GetBodyName(item.Body), GetBodyName(selectedBody), StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }

            return null;
        }

        private static string ResolveInitialFolder(string savedFolder, string modelPath)
        {
            if (!string.IsNullOrWhiteSpace(savedFolder) && Directory.Exists(savedFolder))
            {
                return savedFolder;
            }

            if (!string.IsNullOrWhiteSpace(modelPath))
            {
                string modelFolder = Path.GetDirectoryName(modelPath);
                if (!string.IsNullOrWhiteSpace(modelFolder) && Directory.Exists(modelFolder))
                {
                    return modelFolder;
                }
            }

            return System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
        }

        internal static string CreateNextVersionFileName(string destinationDirectory, string fileStem, ExportFormat format)
        {
            Directory.CreateDirectory(destinationDirectory);

            fileStem = SanitizeFileName(fileStem);
            string extension = format == ExportFormat.Stl ? ".STL" : ".STEP";
            string pattern = "^" + Regex.Escape(fileStem) + "_V(?<version>\\d+)$";

            int nextVersion = 1;
            foreach (string file in Directory.EnumerateFiles(destinationDirectory, fileStem + "_V*" + extension))
            {
                string nameWithoutExtension = Path.GetFileNameWithoutExtension(file);
                Match match = Regex.Match(nameWithoutExtension, pattern, RegexOptions.IgnoreCase);
                int existingVersion;
                if (match.Success && int.TryParse(match.Groups["version"].Value, out existingVersion))
                {
                    nextVersion = Math.Max(nextVersion, existingVersion + 1);
                }
            }

            return fileStem + "_V" + nextVersion.ToString("000") + extension;
        }

        private static string CreateExportPath(string destinationDirectory, string fileName, ExportFormat format)
        {
            Directory.CreateDirectory(destinationDirectory);
            return Path.Combine(destinationDirectory, NormalizeFileName(fileName, format));
        }

        private static string CreateTemporaryExportPath(string fileName, ExportFormat format)
        {
            string destinationDirectory = GetTemporaryExportDirectory();
            string fileStem = StripVersionSuffix(SanitizeFileName(Path.GetFileNameWithoutExtension(fileName)));
            return Path.Combine(destinationDirectory, CreateNextVersionFileName(destinationDirectory, fileStem, format));
        }

        private static string GetTemporaryExportDirectory()
        {
            string directory = Path.Combine(Path.GetTempPath(), "SwPrototypeExporter", "Exports");
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void CleanOldTemporaryExports()
        {
            string directory = GetTemporaryExportDirectory();
            DateTime cutoff = DateTime.Now.AddDays(-7);

            try
            {
                foreach (string file in Directory.EnumerateFiles(directory))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.LastWriteTime < cutoff)
                        {
                            info.Delete();
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        internal static string NormalizeFileName(string fileName, ExportFormat format)
        {
            string extension = format == ExportFormat.Stl ? ".STL" : ".STEP";
            string stem = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrWhiteSpace(stem))
            {
                stem = "Body";
            }

            return SanitizeFileName(stem) + extension;
        }

        private static string SanitizeFileName(string value)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            string sanitized = new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
            sanitized = Regex.Replace(sanitized, "_+", "_").Trim('_', ' ');
            return string.IsNullOrWhiteSpace(sanitized) ? "Body" : sanitized;
        }

        private static string StripVersionSuffix(string fileStem)
        {
            string stripped = Regex.Replace(fileStem ?? string.Empty, "_V\\d+$", string.Empty, RegexOptions.IgnoreCase);
            return string.IsNullOrWhiteSpace(stripped) ? "Body" : stripped;
        }

        private void ExportSelectedBody(ModelDoc2 model, BodyExportItem item, string exportPath, ExportFormat format)
        {
            if (format == ExportFormat.Stl || item.RequiresTemporaryPart)
            {
                ExportBodyThroughTemporaryPart(model, item.Body, exportPath, format);
                return;
            }

            ExportStepSelectedBodyWithSaveAs(model, item.Body, exportPath);
        }

        private void ExportStepSelectedBodyWithSaveAs(ModelDoc2 model, Body2 body, string exportPath)
        {
            int previousStepAp = _swApp.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swStepAP);

            try
            {
                Log("Setting STEP export protocol to AP214.");
                _swApp.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swStepAP, 214);
                ExportSelectedBodyWithSaveAs(model, body, exportPath);
            }
            finally
            {
                try
                {
                    _swApp.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swStepAP, previousStepAp);
                }
                catch
                {
                }
            }
        }

        private static void ExportSelectedBodyWithSaveAs(ModelDoc2 model, Body2 body, string exportPath)
        {
            model.ClearSelection2(true);

            if (!body.Select2(false, null))
            {
                throw new InvalidOperationException("Could not select the requested body for export.");
            }

            int errors = 0;
            int warnings = 0;
            bool saved = model.Extension.SaveAs(
                exportPath,
                (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                null,
                ref errors,
                ref warnings);

            model.ClearSelection2(true);

            if (!saved || errors != 0)
            {
                throw new InvalidOperationException("SOLIDWORKS could not export the body. Error code: " + errors + ", warning code: " + warnings + ".");
            }
        }

        private void ExportBodyThroughTemporaryPart(ModelDoc2 originalModel, Body2 body, string exportPath, ExportFormat format)
        {
            ExportBodiesThroughTemporaryPart(originalModel, new[] { body }, exportPath, format);
        }

        private void ExportBodiesThroughTemporaryPart(ModelDoc2 originalModel, IEnumerable<Body2> bodies, string exportPath, ExportFormat format)
        {
            Log("Exporting " + format + " through temporary part. File: " + exportPath);

            List<Body2> sourceBodies = bodies.Where(body => body != null).ToList();
            if (sourceBodies.Count == 0)
            {
                throw new InvalidOperationException("No bodies were available to export.");
            }

            string originalTitle = originalModel.GetTitle();
            bool previousDocumentVisible = _swApp.GetDocumentVisible((int)swDocumentTypes_e.swDocPART);
            int previousStepAp = _swApp.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swStepAP);
            bool previousBinaryFormat = _swApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLBinaryFormat);
            bool previousShowInfo = _swApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLShowInfoOnSave);
            bool previousTranslatePositive = _swApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLDontTranslateToPositive);
            int previousQuality = _swApp.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swSTLQuality);

            ModelDoc2 tempModel = null;
            string tempTitle = null;

            try
            {
                _swApp.DocumentVisible(false, (int)swDocumentTypes_e.swDocPART);
                tempModel = _swApp.NewPart() as ModelDoc2;
                if (tempModel == null)
                {
                    string template = _swApp.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
                    tempModel = _swApp.NewDocument(template, 0, 0, 0) as ModelDoc2;
                }

                if (tempModel == null)
                {
                    throw new InvalidOperationException("Could not create a temporary part for STL export.");
                }

                tempTitle = tempModel.GetTitle();
                PartDoc tempPart = tempModel as PartDoc;
                if (tempPart == null)
                {
                    throw new InvalidOperationException("Temporary document is not a part document.");
                }

                foreach (Body2 sourceBody in sourceBodies)
                {
                    Body2 copiedBody = sourceBody.Copy2(true) as Body2;
                    if (copiedBody == null)
                    {
                        copiedBody = sourceBody.Copy() as Body2;
                    }

                    if (copiedBody == null)
                    {
                        throw new InvalidOperationException("Could not copy one of the selected bodies for export.");
                    }

                    Feature bodyFeature = tempPart.ICreateFeatureFromBody4(
                        copiedBody,
                        false,
                        (int)swCreateFeatureBodyOpts_e.swCreateFeatureBodyCheck);

                    if (bodyFeature == null)
                    {
                        throw new InvalidOperationException("Could not create a temporary body feature for export.");
                    }
                }

                if (format == ExportFormat.Stl)
                {
                    _swApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLBinaryFormat, true);
                    _swApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLShowInfoOnSave, false);
                    _swApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLDontTranslateToPositive, true);
                    _swApp.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swSTLQuality, (int)swSTLQuality_e.swSTLQuality_Fine);
                }
                else
                {
                    Log("Setting STEP export protocol to AP214.");
                    _swApp.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swStepAP, 214);
                }

                ExportModelWithSaveAs(tempModel, exportPath);

                var info = new FileInfo(exportPath);
                if (format == ExportFormat.Stl && (!info.Exists || info.Length <= 84))
                {
                    throw new InvalidOperationException("SOLIDWORKS created an empty STL file. File size: " + (info.Exists ? info.Length.ToString() : "missing") + " bytes.");
                }
            }
            finally
            {
                try
                {
                    _swApp.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swStepAP, previousStepAp);
                    _swApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLBinaryFormat, previousBinaryFormat);
                    _swApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLShowInfoOnSave, previousShowInfo);
                    _swApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLDontTranslateToPositive, previousTranslatePositive);
                    _swApp.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swSTLQuality, previousQuality);
                    _swApp.DocumentVisible(previousDocumentVisible, (int)swDocumentTypes_e.swDocPART);
                }
                catch
                {
                }

                if (!string.IsNullOrWhiteSpace(tempTitle))
                {
                    try
                    {
                        _swApp.CloseDoc(tempTitle);
                    }
                    catch
                    {
                    }
                }

                if (!string.IsNullOrWhiteSpace(originalTitle))
                {
                    try
                    {
                        int activateErrors = 0;
                        _swApp.ActivateDoc2(originalTitle, true, ref activateErrors);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static void ExportModelWithSaveAs(ModelDoc2 model, string exportPath)
        {
            int errors = 0;
            int warnings = 0;
            bool saved = model.Extension.SaveAs(
                exportPath,
                (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                null,
                ref errors,
                ref warnings);

            if (!saved || errors != 0)
            {
                throw new InvalidOperationException("SOLIDWORKS could not export the file. Error code: " + errors + ", warning code: " + warnings + ".");
            }
        }

        private static bool IsComponentUnavailable(Component2 component)
        {
            try
            {
                if (component.GetSuppression2() == (int)swComponentSuppressionState_e.swComponentSuppressed)
                {
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                return component.IsHidden(true);
            }
            catch
            {
                return false;
            }
        }

        internal static string GetBodyName(Body2 body)
        {
            if (body == null || string.IsNullOrWhiteSpace(body.Name))
            {
                return "Body";
            }

            return body.Name;
        }

        internal static string GetComponentName(Component2 component)
        {
            if (component == null)
            {
                return "Component";
            }

            if (!string.IsNullOrWhiteSpace(component.Name2))
            {
                return component.Name2;
            }

            if (!string.IsNullOrWhiteSpace(component.Name))
            {
                return component.Name;
            }

            string path = component.GetPathName();
            if (!string.IsNullOrWhiteSpace(path))
            {
                return Path.GetFileNameWithoutExtension(path);
            }

            return "Component";
        }

        private static void LaunchSlicer(string slicerExecutable, string exportedFile)
        {
            LaunchSlicer(slicerExecutable, new[] { exportedFile });
        }

        private static void LaunchSlicer(string slicerExecutable, IEnumerable<string> exportedFiles)
        {
            if (!File.Exists(slicerExecutable))
            {
                MessageBox.Show("The slicer executable was not found:\r\n" + slicerExecutable, DialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            List<string> files = exportedFiles.Where(File.Exists).ToList();
            if (files.Count == 0)
            {
                return;
            }

            string arguments = string.Join(" ", files.Select(file => "\"" + file + "\"").ToArray());
            Log("Launching slicer. Exe: " + slicerExecutable + " Files: " + arguments);

            Process.Start(new ProcessStartInfo
            {
                FileName = slicerExecutable,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(slicerExecutable),
                UseShellExecute = true
            });
        }

        private static void WaitForExportedFile(string exportedFile)
        {
            DateTime timeout = DateTime.UtcNow.AddSeconds(10);
            long lastLength = -1;
            int stableReads = 0;

            while (DateTime.UtcNow < timeout)
            {
                try
                {
                    var info = new FileInfo(exportedFile);
                    if (info.Exists && info.Length > 0)
                    {
                        using (FileStream stream = File.Open(exportedFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            if (info.Length == lastLength)
                            {
                                stableReads++;
                                if (stableReads >= 2)
                                {
                                    Log("Exported file is ready. File: " + exportedFile + " Size: " + info.Length);
                                    return;
                                }
                            }
                            else
                            {
                                stableReads = 0;
                                lastLength = info.Length;
                            }
                        }
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }

                Thread.Sleep(250);
            }

            Log("Timed out while waiting for exported file readiness. File: " + exportedFile);
        }

        internal static void Log(string message)
        {
            try
            {
                string directory = Path.GetDirectoryName(LogPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(LogPath, DateTime.Now.ToString("s") + " " + message + System.Environment.NewLine);
            }
            catch
            {
            }
        }

        private static string LogPath
        {
            get
            {
                return Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                    "SwPrototypeExporter",
                    "addin.log");
            }
        }
    }

    internal sealed class ExportContext
    {
        public ExportContext(ModelDoc2 model, IReadOnlyList<BodyExportItem> bodies, BodyExportItem initiallySelectedBody, string initialFolder, SlicerSettings settings)
        {
            Model = model;
            Bodies = bodies;
            InitiallySelectedBody = initiallySelectedBody;
            InitialFolder = initialFolder;
            Settings = settings;
        }

        public ModelDoc2 Model { get; private set; }
        public IReadOnlyList<BodyExportItem> Bodies { get; private set; }
        public BodyExportItem InitiallySelectedBody { get; private set; }
        public string InitialFolder { get; private set; }
        public SlicerSettings Settings { get; private set; }
    }

    internal sealed class ExportRequest
    {
        public ExportRequest(
            ModelDoc2 model,
            IEnumerable<BodyExportItem> selectedItems,
            string destinationDirectory,
            string fileName,
            ExportFormat format,
            string slicerExecutable,
            bool launchSlicer,
            bool exportSeparateFiles,
            bool useTemporaryFile)
        {
            Model = model;
            SelectedItems = selectedItems == null ? new List<BodyExportItem>() : selectedItems.Where(item => item != null).ToList();
            DestinationDirectory = destinationDirectory;
            FileName = fileName;
            Format = format;
            SlicerExecutable = slicerExecutable;
            LaunchSlicer = launchSlicer;
            ExportSeparateFiles = exportSeparateFiles;
            UseTemporaryFile = useTemporaryFile;
        }

        public ModelDoc2 Model { get; private set; }
        public IReadOnlyList<BodyExportItem> SelectedItems { get; private set; }
        public string DestinationDirectory { get; private set; }
        public string FileName { get; private set; }
        public ExportFormat Format { get; private set; }
        public string SlicerExecutable { get; private set; }
        public bool LaunchSlicer { get; private set; }
        public bool ExportSeparateFiles { get; private set; }
        public bool UseTemporaryFile { get; private set; }
    }
}
