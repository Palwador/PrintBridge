using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;

namespace SwPrototypeExporter
{
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class ExportPropertyManagerPage : PropertyManagerPage2Handler9
    {
        private const int GroupBodies = 10;
        private const int GroupOutput = 20;
        private const int GroupSlicer = 30;
        private const int ControlFormat = 201;
        private const int ControlFolder = 202;
        private const int ControlBrowseFolder = 203;
        private const int ControlFileName = 204;
        private const int ControlSeparateFiles = 205;
        private const int ControlUseTemporaryFile = 206;
        private const int ControlSlicer = 301;
        private const int ControlBrowseSlicer = 302;
        private const int ControlLaunchSlicer = 303;
        private const int BodyCheckboxIdStart = 1000;
        private const int SelectionMark = 777;
        private const string TemporaryExportHelpTitle = "Temporary export file";
        private const string TemporaryExportHelpText =
            "Creates the STL or STEP in a temporary folder and sends that file to the slicer. Use this to prototype quickly without saving extra export files next to the SOLIDWORKS model.";

        private static readonly List<ExportPropertyManagerPage> LivePages = new List<ExportPropertyManagerPage>();

        private readonly ExportWorkflow _workflow;
        private readonly ExportContext _context;
        private readonly ISldWorks _swApp;
        private readonly List<int> _selectedIndices = new List<int>();
        private readonly List<BodyAppearanceSnapshot> _previewedBodies = new List<BodyAppearanceSnapshot>();
        private readonly Dictionary<int, int> _checkboxBodyIndices = new Dictionary<int, int>();
        private readonly List<IPropertyManagerPageCheckbox> _bodyCheckboxes = new List<IPropertyManagerPageCheckbox>();
        private List<SlicerChoice> _slicers = new List<SlicerChoice>();
        private IPropertyManagerPage2 _page;
        private IPropertyManagerPageCombobox _formatCombo;
        private IPropertyManagerPageTextbox _folderText;
        private IPropertyManagerPageButton _browseFolderButton;
        private IPropertyManagerPageTextbox _fileNameText;
        private IPropertyManagerPageCheckbox _separateFilesCheck;
        private IPropertyManagerPageCheckbox _useTemporaryFileCheck;
        private IPropertyManagerPageCombobox _slicerCombo;
        private IPropertyManagerPageCheckbox _launchSlicerCheck;
        private DPartDocEvents_Event _partEvents;
        private DAssemblyDocEvents_Event _assemblyEvents;
        private bool _updatingControls;
        private bool _fileNameEdited;
        private bool _handlingDocumentSelection;

        internal ExportPropertyManagerPage(ExportWorkflow workflow, ExportContext context)
        {
            _workflow = workflow;
            _context = context;
            _swApp = workflow.SwApp;
        }

        public void Show()
        {
            int errors = 0;
            int options =
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_OkayButton |
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_CancelButton |
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_CanEscapeCancel |
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_AllowHorizontalResize;

            _page = _swApp.CreatePropertyManagerPage(ExportWorkflow.DialogTitle, options, this, ref errors) as IPropertyManagerPage2;
            if (_page == null || errors != 0)
            {
                throw new InvalidOperationException("Could not create the SOLIDWORKS PropertyManager page. Error code: " + errors + ".");
            }

            BuildControls();
            SeedInitialValues();
            HookDocumentEvents();

            LivePages.Add(this);
            _page.Show();
        }

        private void BuildControls()
        {
            int groupOptions =
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible |
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded;
            int controlOptions =
                (int)swAddControlOptions_e.swControlOptions_Visible |
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            short left = (short)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;

            IPropertyManagerPageGroup bodyGroup = _page.AddGroupBox(GroupBodies, "Bodies", groupOptions) as IPropertyManagerPageGroup;
            for (int i = 0; i < _context.Bodies.Count; i++)
            {
                int checkboxId = BodyCheckboxIdStart + i;
                IPropertyManagerPageCheckbox checkbox = bodyGroup.AddControl2(
                    checkboxId,
                    (short)swPropertyManagerPageControlType_e.swControlType_Checkbox,
                    _context.Bodies[i].DisplayName,
                    left,
                    controlOptions,
                    "Check to include this body in the export.") as IPropertyManagerPageCheckbox;

                _checkboxBodyIndices[checkboxId] = i;
                _bodyCheckboxes.Add(checkbox);
            }

            IPropertyManagerPageGroup outputGroup = _page.AddGroupBox(GroupOutput, "Output", groupOptions) as IPropertyManagerPageGroup;
            _formatCombo = outputGroup.AddControl2(
                ControlFormat,
                (short)swPropertyManagerPageControlType_e.swControlType_Combobox,
                "Format",
                left,
                controlOptions,
                "Choose STL or STEP AP214.") as IPropertyManagerPageCombobox;
            _formatCombo.Style = (int)swPropMgrPageComboBoxStyle_e.swPropMgrPageComboBoxStyle_EditBoxReadOnly;
            _formatCombo.AddItems(new[] { "STL", "STEP AP214" });

            _folderText = outputGroup.AddControl2(
                ControlFolder,
                (short)swPropertyManagerPageControlType_e.swControlType_Textbox,
                "Folder",
                left,
                controlOptions,
                "Destination folder.") as IPropertyManagerPageTextbox;

            _browseFolderButton = outputGroup.AddControl2(
                ControlBrowseFolder,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Browse folder",
                left,
                controlOptions,
                "Browse for an export folder.") as IPropertyManagerPageButton;

            _fileNameText = outputGroup.AddControl2(
                ControlFileName,
                (short)swPropertyManagerPageControlType_e.swControlType_Textbox,
                "File name",
                left,
                controlOptions,
                "Export file name.") as IPropertyManagerPageTextbox;

            _separateFilesCheck = outputGroup.AddControl2(
                ControlSeparateFiles,
                (short)swPropertyManagerPageControlType_e.swControlType_Checkbox,
                "Export selected bodies as separate files",
                left,
                controlOptions,
                "When checked, each selected body gets its own versioned file.") as IPropertyManagerPageCheckbox;

            _useTemporaryFileCheck = outputGroup.AddControl2(
                ControlUseTemporaryFile,
                (short)swPropertyManagerPageControlType_e.swControlType_Checkbox,
                "Use temporary export file",
                left,
                controlOptions,
                TemporaryExportHelpText) as IPropertyManagerPageCheckbox;
            SetControlPictureLabel(_useTemporaryFileCheck, EnsureTemporaryHelpIconFiles());

            IPropertyManagerPageGroup slicerGroup = _page.AddGroupBox(GroupSlicer, "Slicer", groupOptions) as IPropertyManagerPageGroup;
            _slicerCombo = slicerGroup.AddControl2(
                ControlSlicer,
                (short)swPropertyManagerPageControlType_e.swControlType_Combobox,
                "Slicer",
                left,
                controlOptions,
                "Choose a detected slicer.") as IPropertyManagerPageCombobox;
            _slicerCombo.Style = (int)swPropMgrPageComboBoxStyle_e.swPropMgrPageComboBoxStyle_EditBoxReadOnly;

            slicerGroup.AddControl2(
                ControlBrowseSlicer,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Browse slicer",
                left,
                controlOptions,
                "Browse for a slicer executable.");

            _launchSlicerCheck = slicerGroup.AddControl2(
                ControlLaunchSlicer,
                (short)swPropertyManagerPageControlType_e.swControlType_Checkbox,
                "Open in slicer after export",
                left,
                controlOptions,
                "Launch the selected slicer after exporting.") as IPropertyManagerPageCheckbox;
        }

        private void SeedInitialValues()
        {
            _updatingControls = true;
            try
            {
                _folderText.Text = _context.InitialFolder;
                _formatCombo.CurrentSelection = string.Equals(_context.Settings.LastFormat, ExportFormat.Step.ToString(), StringComparison.OrdinalIgnoreCase)
                    ? (short)1
                    : (short)0;
                _launchSlicerCheck.Checked = _context.Settings.LaunchSlicer;
                _useTemporaryFileCheck.Checked = _context.Settings.UseTemporaryFile;
                PopulateSlicers(_context.Settings.SlicerPath);

                SyncControlsFromSelection();
                UpdateOutputControlState();
                UpdateFileNameFromSelection();
            }
            finally
            {
                _updatingControls = false;
            }
        }

        private void PopulateSlicers(string preferredPath)
        {
            _slicers = SlicerDiscovery.FindInstalledSlicers(preferredPath);
            _slicerCombo.Clear();

            if (_slicers.Count == 0)
            {
                _slicerCombo.AddItems(new[] { "No slicer found" });
                _slicerCombo.CurrentSelection = 0;
                return;
            }

            _slicerCombo.AddItems(_slicers.Select(slicer => slicer.Name).ToArray());
            _slicerCombo.CurrentSelection = 0;
        }

        private void HookDocumentEvents()
        {
            try
            {
                if (_context.Model.GetType() == (int)swDocumentTypes_e.swDocPART)
                {
                    _partEvents = _context.Model as DPartDocEvents_Event;
                    if (_partEvents != null)
                    {
                        _partEvents.UserSelectionPostNotify += OnUserSelectionPostNotify;
                    }
                }
                else if (_context.Model.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY)
                {
                    _assemblyEvents = _context.Model as DAssemblyDocEvents_Event;
                    if (_assemblyEvents != null)
                    {
                        _assemblyEvents.UserSelectionPostNotify += OnUserSelectionPostNotify;
                    }
                }
            }
            catch (Exception ex)
            {
                ExportWorkflow.Log("Could not hook document selection events: " + ex);
            }
        }

        private void UnhookDocumentEvents()
        {
            try
            {
                if (_partEvents != null)
                {
                    _partEvents.UserSelectionPostNotify -= OnUserSelectionPostNotify;
                    _partEvents = null;
                }

                if (_assemblyEvents != null)
                {
                    _assemblyEvents.UserSelectionPostNotify -= OnUserSelectionPostNotify;
                    _assemblyEvents = null;
                }
            }
            catch (Exception ex)
            {
                ExportWorkflow.Log("Could not unhook document selection events: " + ex);
            }
        }

        private void SyncControlsFromSelection()
        {
            _updatingControls = true;
            try
            {
                for (int i = 0; i < _bodyCheckboxes.Count; i++)
                {
                    _bodyCheckboxes[i].Checked = _selectedIndices.Contains(i);
                }

                ShowBodyPreview();
                _page.EnableButton((int)swPropertyManagerPageButtons_e.swPropertyManagerPageButton_Ok, _selectedIndices.Count > 0);
            }
            finally
            {
                _updatingControls = false;
            }
        }

        private void ToggleBodySelection(int index)
        {
            if (index < 0 || index >= _context.Bodies.Count)
            {
                return;
            }

            if (_selectedIndices.Contains(index))
            {
                _selectedIndices.Remove(index);
            }
            else
            {
                _selectedIndices.Add(index);
            }

            SyncControlsFromSelection();
            UpdateFileNameFromSelection();
        }

        private void UpdateOutputControlState()
        {
            bool useTemporaryFile = _useTemporaryFileCheck != null && _useTemporaryFileCheck.Checked;

            SetControlEnabled(_folderText, !useTemporaryFile);
            SetControlEnabled(_browseFolderButton, !useTemporaryFile);
        }

        private static void SetControlEnabled(object control, bool enabled)
        {
            var pageControl = control as IPropertyManagerPageControl;
            if (pageControl != null)
            {
                pageControl.Enabled = enabled;
            }
        }

        private static void SetControlPictureLabel(object control, HelpIconFileSet icons)
        {
            var pageControl = control as IPropertyManagerPageControl;
            if (pageControl == null || icons == null)
            {
                return;
            }

            try
            {
                pageControl.SetPictureLabelByName(icons.ColorBitmapPath, icons.MaskBitmapPath);
            }
            catch (Exception ex)
            {
                ExportWorkflow.Log("Could not set temporary export help picture label: " + ex);
            }
        }

        private static HelpIconFileSet EnsureTemporaryHelpIconFiles()
        {
            string assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string iconDirectory = Path.Combine(assemblyDirectory, "icons");
            Directory.CreateDirectory(iconDirectory);

            string colorPath = Path.Combine(iconDirectory, "temporary-export-help.bmp");
            string maskPath = Path.Combine(iconDirectory, "temporary-export-help-mask.bmp");
            if (!File.Exists(colorPath) || !File.Exists(maskPath))
            {
                CreateTemporaryHelpIcon(colorPath, maskPath);
            }

            return new HelpIconFileSet(colorPath, maskPath);
        }

        private static void CreateTemporaryHelpIcon(string colorPath, string maskPath)
        {
            using (var color = new Bitmap(16, 16, PixelFormat.Format24bppRgb))
            using (Graphics graphics = Graphics.FromImage(color))
            {
                graphics.Clear(Color.White);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var fill = new SolidBrush(Color.FromArgb(225, 241, 255)))
                using (var outline = new Pen(Color.FromArgb(43, 122, 189), 1.4f))
                using (var textBrush = new SolidBrush(Color.FromArgb(20, 86, 150)))
                using (var font = new Font("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    graphics.FillEllipse(fill, 1, 1, 13, 13);
                    graphics.DrawEllipse(outline, 1, 1, 13, 13);
                    graphics.DrawString("?", font, textBrush, 4.6f, 1.6f);
                }

                color.Save(colorPath, ImageFormat.Bmp);
            }

            using (var mask = new Bitmap(16, 16, PixelFormat.Format24bppRgb))
            using (Graphics graphics = Graphics.FromImage(mask))
            {
                graphics.Clear(Color.White);
                using (var brush = new SolidBrush(Color.Black))
                {
                    graphics.FillEllipse(brush, 1, 1, 13, 13);
                }

                mask.Save(maskPath, ImageFormat.Bmp);
            }
        }

        private void UpdateFileNameFromSelection()
        {
            if (_fileNameEdited)
            {
                return;
            }

            string fileStem;
            if (_selectedIndices.Count == 1)
            {
                fileStem = _context.Bodies[_selectedIndices[0]].FileStemName;
            }
            else if (!string.IsNullOrWhiteSpace(_context.Model.GetPathName()))
            {
                fileStem = Path.GetFileNameWithoutExtension(_context.Model.GetPathName());
            }
            else
            {
                fileStem = "Bodies";
            }

            try
            {
                _fileNameText.Text = ExportWorkflow.CreateNextVersionFileName(
                    _folderText.Text,
                    fileStem,
                    GetSelectedFormat());
            }
            catch
            {
                _fileNameText.Text = ExportWorkflow.NormalizeFileName(fileStem, GetSelectedFormat());
            }
        }

        private void ShowBodyPreview()
        {
            try
            {
                RestoreBodyPreview();

                foreach (int index in _selectedIndices)
                {
                    BodyExportItem item = _context.Bodies[index];
                    CaptureOriginalAppearance(item.Body);
                    item.Body.MaterialPropertyValues2 = CreateHighlightAppearance();
                }

                _context.Model.GraphicsRedraw2();
            }
            catch (Exception ex)
            {
                ExportWorkflow.Log("Temporary body preview failed: " + ex);
            }
        }

        private void CaptureOriginalAppearance(Body2 body)
        {
            if (body == null || _previewedBodies.Any(snapshot => ReferenceEquals(snapshot.Body, body)))
            {
                return;
            }

            _previewedBodies.Add(new BodyAppearanceSnapshot(
                body,
                body.HasMaterialPropertyValues(),
                CloneAppearance(body.MaterialPropertyValues2)));
        }

        private void RestoreBodyPreview()
        {
            foreach (BodyAppearanceSnapshot snapshot in _previewedBodies)
            {
                try
                {
                    if (snapshot.HadBodyAppearance)
                    {
                        snapshot.Body.MaterialPropertyValues2 = CloneAppearance(snapshot.MaterialPropertyValues);
                    }
                    else
                    {
                        snapshot.Body.RemoveMaterialProperty((int)swInConfigurationOpts_e.swThisConfiguration, null);
                    }
                }
                catch (Exception ex)
                {
                    ExportWorkflow.Log("Could not restore preview body appearance: " + ex);
                }
            }

            _previewedBodies.Clear();
        }

        private static object CloneAppearance(object appearance)
        {
            var values = appearance as double[];
            if (values != null)
            {
                return values.ToArray();
            }

            var array = appearance as Array;
            if (array == null)
            {
                return appearance;
            }

            var clone = new double[array.Length];
            for (int i = 0; i < array.Length; i++)
            {
                clone[i] = Convert.ToDouble(array.GetValue(i));
            }

            return clone;
        }

        private static double[] CreateHighlightAppearance()
        {
            return new[]
            {
                1.0, 0.70, 0.0,
                0.4, 0.8, 0.35,
                0.25, 0.0, 0.0
            };
        }

        private int FindBodyIndex(object selectionObject)
        {
            return FindBodyIndex(selectionObject, null);
        }

        private int FindBodyIndex(object selectionObject, Component2 selectedComponent)
        {
            Body2 body = selectionObject as Body2;
            Component2 component = selectedComponent ?? selectionObject as Component2;

            var face = selectionObject as Face2;
            if (face != null)
            {
                body = face.GetBody() as Body2;
            }

            var entity = selectionObject as Entity;
            if (entity != null && component == null)
            {
                try
                {
                    component = entity.GetComponent() as Component2;
                }
                catch
                {
                }
            }

            if (body == null && component != null)
            {
                body = component.GetBody() as Body2;
            }

            if (body == null)
            {
                return -1;
            }

            return FindBodyIndex(body, component);
        }

        private int FindBodyIndex(Body2 body, Component2 component)
        {
            if (body == null)
            {
                return -1;
            }

            string bodyName = ExportWorkflow.GetBodyName(body);
            string componentName = component == null ? null : ExportWorkflow.GetComponentName(component);

            for (int i = 0; i < _context.Bodies.Count; i++)
            {
                BodyExportItem item = _context.Bodies[i];
                bool bodyMatches = string.Equals(ExportWorkflow.GetBodyName(item.Body), bodyName, StringComparison.OrdinalIgnoreCase);
                if (!bodyMatches)
                {
                    continue;
                }

                if (item.Component == null && component == null)
                {
                    return i;
                }

                if (item.Component != null && componentName != null
                    && string.Equals(ExportWorkflow.GetComponentName(item.Component), componentName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private ExportFormat GetSelectedFormat()
        {
            return _formatCombo.CurrentSelection == 1 ? ExportFormat.Step : ExportFormat.Stl;
        }

        private string GetSelectedSlicerPath()
        {
            int index = _slicerCombo.CurrentSelection;
            if (index >= 0 && index < _slicers.Count)
            {
                return _slicers[index].ExecutablePath;
            }

            return string.Empty;
        }

        private void BrowseFolder()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Choose export folder";
                dialog.SelectedPath = Directory.Exists(_folderText.Text) ? _folderText.Text : _context.InitialFolder;
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    _folderText.Text = dialog.SelectedPath;
                    UpdateFileNameFromSelection();
                }
            }
        }

        private void BrowseSlicer()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "Choose slicer executable";
                dialog.Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    PopulateSlicers(dialog.FileName);
                    _launchSlicerCheck.Checked = true;
                }
            }
        }

        private void RunExport()
        {
            RestoreBodyPreview();
            _context.Model.GraphicsRedraw2();

            var selectedItems = _selectedIndices
                .Where(index => index >= 0 && index < _context.Bodies.Count)
                .Select(index => _context.Bodies[index])
                .ToList();

            _workflow.Export(new ExportRequest(
                _context.Model,
                selectedItems,
                _folderText.Text,
                ExportWorkflow.NormalizeFileName(_fileNameText.Text, GetSelectedFormat()),
                GetSelectedFormat(),
                GetSelectedSlicerPath(),
                _launchSlicerCheck.Checked,
                _separateFilesCheck.Checked,
                _useTemporaryFileCheck.Checked));
        }

        private static int ToSolidWorksColor(int red, int green, int blue)
        {
            return red | (green << 8) | (blue << 16);
        }

        private int OnUserSelectionPostNotify()
        {
            if (_updatingControls || _handlingDocumentSelection)
            {
                return 0;
            }

            _handlingDocumentSelection = true;
            try
            {
                SelectionMgr selectionMgr = _context.Model.SelectionManager as SelectionMgr;
                if (selectionMgr == null)
                {
                    return 0;
                }

                int count = selectionMgr.GetSelectedObjectCount2(-1);
                if (count < 1)
                {
                    return 0;
                }

                object selectedObject = selectionMgr.GetSelectedObject6(count, -1);
                Component2 selectedComponent = null;
                try
                {
                    selectedComponent = selectionMgr.GetSelectedObjectsComponent3(count, -1);
                }
                catch
                {
                }

                int index = FindBodyIndex(selectedObject, selectedComponent);
                if (index >= 0)
                {
                    ToggleBodySelection(index);
                }
            }
            catch (Exception ex)
            {
                ExportWorkflow.Log("Document selection handling failed: " + ex);
            }
            finally
            {
                try
                {
                    _context.Model.ClearSelection2(true);
                }
                catch
                {
                }

                _handlingDocumentSelection = false;
            }

            return 0;
        }
        
        public void OnClose(int Reason)
        {
            if (Reason == (int)swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Okay)
            {
                try
                {
                    RunExport();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, ExportWorkflow.DialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        public void AfterClose()
        {
            try
            {
                UnhookDocumentEvents();
                RestoreBodyPreview();
                _context.Model.ClearSelection2(true);
                _context.Model.GraphicsRedraw2();
            }
            catch
            {
            }

            LivePages.Remove(this);
        }

        public bool OnSubmitSelection(int Id, object Selection, int SelType, ref string ItemText)
        {
            return false;
        }

        public void OnListboxSelectionChanged(int Id, int Item)
        {
        }

        public void OnSelectionboxListChanged(int Id, int Count)
        {
            ExportWorkflow.Log("PropertyManager selection box changed. Id: " + Id + " Count: " + Count);
        }

        public void OnComboboxSelectionChanged(int Id, int Item)
        {
            if (_updatingControls)
            {
                return;
            }

            if (Id == ControlFormat)
            {
                UpdateFileNameFromSelection();
            }
        }

        public void OnTextboxChanged(int Id, string Text)
        {
            if (_updatingControls)
            {
                return;
            }

            if (Id == ControlFileName)
            {
                _fileNameEdited = true;
            }
            else if (Id == ControlFolder)
            {
                UpdateFileNameFromSelection();
            }
        }

        public void OnButtonPress(int Id)
        {
            if (Id == ControlBrowseFolder)
            {
                BrowseFolder();
            }
            else if (Id == ControlBrowseSlicer)
            {
                BrowseSlicer();
            }
        }

        public void AfterActivation() { }
        public int OnActiveXControlCreated(int Id, bool Status) { return 0; }
        public void OnCheckboxCheck(int Id, bool Checked)
        {
            if (_updatingControls)
            {
                return;
            }

            int index;
            if (!_checkboxBodyIndices.TryGetValue(Id, out index))
            {
                if (Id == ControlUseTemporaryFile)
                {
                    UpdateOutputControlState();
                    UpdateFileNameFromSelection();
                }

                return;
            }

            if (Checked)
            {
                if (!_selectedIndices.Contains(index))
                {
                    _selectedIndices.Add(index);
                }
            }
            else
            {
                _selectedIndices.Remove(index);
            }

            SyncControlsFromSelection();
            UpdateFileNameFromSelection();

        }
        public void OnComboboxEditChanged(int Id, string Text) { }
        public void OnGainedFocus(int Id) { }
        public void OnGroupCheck(int Id, bool Checked) { }
        public void OnGroupExpand(int Id, bool Expanded) { }
        public bool OnHelp() { return true; }
        public bool OnKeystroke(int Wparam, int Message, int Lparam, int Id) { return true; }
        public void OnListboxRMBUp(int Id, int PosX, int PosY) { }
        public void OnLostFocus(int Id) { }
        public bool OnNextPage() { return true; }
        public void OnNumberboxChanged(int Id, double Value) { }
        public void OnNumberBoxTrackingCompleted(int Id, double Value) { }
        public void OnOptionCheck(int Id) { }
        public void OnPopupMenuItem(int Id) { }
        public void OnPopupMenuItemUpdate(int Id, ref int retval) { }
        public bool OnPreview() { return true; }
        public bool OnPreviousPage() { return true; }
        public void OnRedo() { }
        public void OnSelectionboxCalloutCreated(int Id) { }
        public void OnSelectionboxCalloutDestroyed(int Id) { }
        public void OnSelectionboxFocusChanged(int Id) { }
        public void OnSliderPositionChanged(int Id, double Value) { }
        public void OnSliderTrackingCompleted(int Id, double Value) { }
        public bool OnTabClicked(int Id) { return true; }
        public void OnUndo() { }
        public void OnWhatsNew() { }
        public int OnWindowFromHandleControlCreated(int Id, bool Status) { return 0; }

        private sealed class BodyAppearanceSnapshot
        {
            public BodyAppearanceSnapshot(Body2 body, bool hadBodyAppearance, object materialPropertyValues)
            {
                Body = body;
                HadBodyAppearance = hadBodyAppearance;
                MaterialPropertyValues = materialPropertyValues;
            }

            public Body2 Body { get; private set; }
            public bool HadBodyAppearance { get; private set; }
            public object MaterialPropertyValues { get; private set; }
        }

        private sealed class HelpIconFileSet
        {
            public HelpIconFileSet(string colorBitmapPath, string maskBitmapPath)
            {
                ColorBitmapPath = colorBitmapPath;
                MaskBitmapPath = maskBitmapPath;
            }

            public string ColorBitmapPath { get; private set; }
            public string MaskBitmapPath { get; private set; }
        }
    }
}
