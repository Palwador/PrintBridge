using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;

namespace SwPrototypeExporter
{
    internal sealed class ExportDialog : Form
    {
        private readonly ComboBox _bodyCombo;
        private readonly RadioButton _stlRadio;
        private readonly RadioButton _stepRadio;
        private readonly TextBox _folderText;
        private readonly TextBox _fileNameText;
        private readonly ComboBox _slicerCombo;
        private readonly CheckBox _launchSlicerCheck;
        private readonly string _modelPath;
        private bool _fileNameWasEdited;
        private bool _updatingSuggestedFileName;

        public ExportDialog(IReadOnlyList<BodyExportItem> bodies, BodyExportItem selectedBody, string initialFolder, string modelPath, SlicerSettings settings)
        {
            _modelPath = modelPath;

            Text = "Export to 3D-printer";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(620, 330);

            var bodyLabel = new Label { Left = 16, Top = 18, Width = 120, Text = "Body" };
            _bodyCombo = new ComboBox { Left = 150, Top = 14, Width = 445, DropDownStyle = ComboBoxStyle.DropDownList };

            foreach (BodyExportItem body in bodies)
            {
                _bodyCombo.Items.Add(new BodyChoice(body));
            }

            int selectedIndex = FindSelectedBodyIndex(bodies, selectedBody);
            _bodyCombo.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;

            var formatLabel = new Label { Left = 16, Top = 58, Width = 120, Text = "Format" };
            _stlRadio = new RadioButton { Left = 150, Top = 56, Width = 80, Text = "STL" };
            _stepRadio = new RadioButton { Left = 245, Top = 56, Width = 90, Text = "STEP" };

            if (string.Equals(settings.LastFormat, ExportFormat.Step.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                _stepRadio.Checked = true;
            }
            else
            {
                _stlRadio.Checked = true;
            }

            var folderLabel = new Label { Left = 16, Top = 98, Width = 120, Text = "Save folder" };
            _folderText = new TextBox { Left = 150, Top = 94, Width = 360, Text = initialFolder };
            var browseFolderButton = new Button { Left = 522, Top = 92, Width = 73, Text = "Browse" };
            browseFolderButton.Click += BrowseFolderButton_Click;

            var fileNameLabel = new Label { Left = 16, Top = 138, Width = 120, Text = "File name" };
            _fileNameText = new TextBox { Left = 150, Top = 134, Width = 445 };
            _fileNameText.TextChanged += FileNameText_TextChanged;
            UpdateSuggestedFileName(true);

            var slicerLabel = new Label { Left = 16, Top = 178, Width = 120, Text = "Slicer app" };
            _slicerCombo = new ComboBox { Left = 150, Top = 174, Width = 360, DropDownStyle = ComboBoxStyle.DropDown };
            PopulateSlicerChoices(settings.SlicerPath);
            var browseSlicerButton = new Button { Left = 522, Top = 172, Width = 73, Text = "Browse" };
            browseSlicerButton.Click += BrowseSlicerButton_Click;

            _launchSlicerCheck = new CheckBox
            {
                Left = 150,
                Top = 214,
                Width = 260,
                Text = "Open exported file in slicer",
                Checked = settings.LaunchSlicer
            };

            var exportButton = new Button { Left = 425, Top = 275, Width = 80, Text = "Export", DialogResult = DialogResult.OK };
            var cancelButton = new Button { Left = 515, Top = 275, Width = 80, Text = "Cancel", DialogResult = DialogResult.Cancel };
            exportButton.Click += ExportButton_Click;

            _bodyCombo.SelectedIndexChanged += ExportChoice_Changed;
            _stlRadio.CheckedChanged += ExportChoice_Changed;
            _stepRadio.CheckedChanged += ExportChoice_Changed;
            _folderText.TextChanged += ExportChoice_Changed;

            Controls.AddRange(new Control[]
            {
                bodyLabel,
                _bodyCombo,
                formatLabel,
                _stlRadio,
                _stepRadio,
                folderLabel,
                _folderText,
                browseFolderButton,
                fileNameLabel,
                _fileNameText,
                slicerLabel,
                _slicerCombo,
                browseSlicerButton,
                _launchSlicerCheck,
                exportButton,
                cancelButton
            });

            AcceptButton = exportButton;
            CancelButton = cancelButton;
        }

        public BodyExportItem SelectedItem
        {
            get { return ((BodyChoice)_bodyCombo.SelectedItem).Item; }
        }

        public ExportFormat Format
        {
            get { return _stepRadio.Checked ? ExportFormat.Step : ExportFormat.Stl; }
        }

        public string DestinationDirectory
        {
            get { return _folderText.Text.Trim(); }
        }

        public string FileName
        {
            get { return ExportWorkflow.NormalizeFileName(_fileNameText.Text.Trim(), Format); }
        }

        public string SlicerExecutable
        {
            get
            {
                var choice = _slicerCombo.SelectedItem as SlicerChoice;
                if (choice != null)
                {
                    return choice.ExecutablePath;
                }

                return _slicerCombo.Text.Trim();
            }
        }

        public bool LaunchSlicer
        {
            get { return _launchSlicerCheck.Checked; }
        }

        private static int FindSelectedBodyIndex(IReadOnlyList<BodyExportItem> bodies, BodyExportItem selectedBody)
        {
            if (selectedBody == null)
            {
                return -1;
            }

            for (int i = 0; i < bodies.Count; i++)
            {
                if (object.ReferenceEquals(bodies[i], selectedBody))
                {
                    return i;
                }
            }

            return -1;
        }

        private void BrowseFolderButton_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.SelectedPath = Directory.Exists(_folderText.Text) ? _folderText.Text : System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _folderText.Text = dialog.SelectedPath;
                    UpdateSuggestedFileName(false);
                }
            }
        }

        private void ExportChoice_Changed(object sender, EventArgs e)
        {
            UpdateSuggestedFileName(false);
        }

        private void FileNameText_TextChanged(object sender, EventArgs e)
        {
            if (!_updatingSuggestedFileName)
            {
                _fileNameWasEdited = true;
            }
        }

        private void UpdateSuggestedFileName(bool force)
        {
            if (!force && _fileNameWasEdited)
            {
                return;
            }

            var selected = _bodyCombo.SelectedItem as BodyChoice;
            if (selected == null || string.IsNullOrWhiteSpace(_folderText.Text))
            {
                return;
            }

            _updatingSuggestedFileName = true;
            try
            {
                _fileNameText.Text = ExportWorkflow.CreateNextVersionFileName(
                    _folderText.Text.Trim(),
                    selected.Item.FileStemName,
                    Format);
                _fileNameWasEdited = false;
            }
            catch
            {
            }
            finally
            {
                _updatingSuggestedFileName = false;
            }
        }

        private void BrowseSlicerButton_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "Applications (*.exe)|*.exe|All files (*.*)|*.*";
                dialog.Title = "Choose slicer application";

                if (!string.IsNullOrWhiteSpace(SlicerExecutable) && File.Exists(SlicerExecutable))
                {
                    dialog.InitialDirectory = Path.GetDirectoryName(SlicerExecutable);
                }

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _slicerCombo.SelectedItem = null;
                    _slicerCombo.Text = dialog.FileName;
                }
            }
        }

        private void PopulateSlicerChoices(string savedSlicerPath)
        {
            List<SlicerChoice> choices = SlicerDiscovery.FindInstalledSlicers(savedSlicerPath);
            foreach (SlicerChoice choice in choices)
            {
                _slicerCombo.Items.Add(choice);
            }

            for (int i = 0; i < _slicerCombo.Items.Count; i++)
            {
                var choice = _slicerCombo.Items[i] as SlicerChoice;
                if (choice != null && string.Equals(choice.ExecutablePath, savedSlicerPath, StringComparison.OrdinalIgnoreCase))
                {
                    _slicerCombo.SelectedIndex = i;
                    return;
                }
            }

            if (_slicerCombo.Items.Count > 0)
            {
                _slicerCombo.SelectedIndex = 0;
            }
            else
            {
                _slicerCombo.Text = savedSlicerPath;
            }
        }

        private void ExportButton_Click(object sender, EventArgs e)
        {
            if (_bodyCombo.SelectedItem == null)
            {
                MessageBox.Show(this, "Choose a body to export.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.None;
                return;
            }

            if (string.IsNullOrWhiteSpace(_folderText.Text))
            {
                MessageBox.Show(this, "Choose a save folder.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.None;
                return;
            }

            if (string.IsNullOrWhiteSpace(_fileNameText.Text))
            {
                MessageBox.Show(this, "Enter a file name.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.None;
                return;
            }

            if (_launchSlicerCheck.Checked && string.IsNullOrWhiteSpace(SlicerExecutable))
            {
                MessageBox.Show(this, "Choose a slicer app or turn off slicer launch.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.None;
                return;
            }

            if (_launchSlicerCheck.Checked && !File.Exists(SlicerExecutable))
            {
                MessageBox.Show(this, "The selected slicer app was not found.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                DialogResult = DialogResult.None;
            }
        }

        private sealed class BodyChoice
        {
            public BodyChoice(BodyExportItem item)
            {
                Item = item;
            }

            public BodyExportItem Item { get; private set; }

            public override string ToString()
            {
                return Item.ToString();
            }
        }
    }
}
