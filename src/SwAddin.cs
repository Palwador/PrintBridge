using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;

namespace SwPrototypeExporter
{
    [ComVisible(true)]
    [Guid("040C231A-2571-4FFC-894D-8D01C2530606")]
    [ProgId("SwPrototypeExporter.Addin")]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public class SwAddin : ISwAddin
    {
        private const int CommandGroupId = 1001;
        private const int ExportCommandItemId = 0;
        private const string AddinTitle = "Export to 3D-printer";
        private const string LegacyAddinTitle = "Prototype Exporter";
        private const string AddinDescription = "Exports selected bodies as STL or STEP and opens them in your slicer.";

        private ISldWorks _swApp;
        private ICommandManager _commandManager;
        private int _addinId;

        public SwAddin()
        {
            Log("SwAddin COM object constructed.");
        }

        public bool ConnectToSW(object thisSw, int cookie)
        {
            Log("ConnectToSW entered. Cookie: " + cookie);
            _swApp = (ISldWorks)thisSw;
            _addinId = cookie;

            RegisterCallbacks();
            try
            {
                Log("Creating CommandManager UI.");
                _commandManager = _swApp.GetCommandManager(_addinId);
                AddCommandManager();
            }
            catch (Exception ex)
            {
                Log("CommandManager setup failed: " + ex);
            }

            try
            {
                Log("Creating fallback menu items.");
                AddFallbackMenuItems();
                Log("Fallback menu setup completed.");
            }
            catch (Exception ex)
            {
                Log("Fallback menu setup failed: " + ex);
                MessageBox.Show("Export to 3D-printer loaded, but SolidWorks did not accept the menu command. Details were written to:\r\n" + LogPath, AddinTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return true;
            }

            return true;
        }

        private void RegisterCallbacks()
        {
            try
            {
                Log("Calling SetAddinCallbackInfo.");
                _swApp.SetAddinCallbackInfo(0, this, _addinId);
                Log("SetAddinCallbackInfo completed.");
            }
            catch (Exception ex)
            {
                Log("SetAddinCallbackInfo failed: " + ex);
                Log("Calling SetAddinCallbackInfo2.");
                _swApp.SetAddinCallbackInfo2(0, this, _addinId);
                Log("SetAddinCallbackInfo2 completed.");
            }
        }

        public bool DisconnectFromSW()
        {
            RemoveCommandManager();

            if (_commandManager != null)
            {
                Marshal.ReleaseComObject(_commandManager);
                _commandManager = null;
            }

            _swApp = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();

            return true;
        }

        public void ExportBodyToSlicer()
        {
            try
            {
                new ExportWorkflow(_swApp).Run();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, AddinTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public int EnableExportBodyToSlicer()
        {
            return 1;
        }

        public int AlwaysEnableExportBodyToSlicer()
        {
            return 1;
        }

        private void AddCommandManager()
        {
            if (_commandManager == null)
            {
                return;
            }

            int errors = 0;
            var commandGroup = _commandManager.CreateCommandGroup2(
                CommandGroupId,
                AddinTitle,
                AddinDescription,
                "Export tools for 3D printing",
                -1,
                true,
                ref errors);

            IconFileSet icons = EnsureIconFiles();
            commandGroup.SmallMainIcon = icons.SmallMainIcon;
            commandGroup.LargeMainIcon = icons.LargeMainIcon;
            commandGroup.SmallIconList = icons.SmallIconList;
            commandGroup.LargeIconList = icons.LargeIconList;

            int itemType = (int)swCommandItemType_e.swToolbarItem;
            commandGroup.AddCommandItem2(
                "Export to 3D-printer",
                -1,
                "Export selected bodies for 3D printing",
                "Export to 3D-printer",
                0,
                "ExportBodyToSlicer",
                "EnableExportBodyToSlicer",
                ExportCommandItemId,
                itemType);

            commandGroup.HasToolbar = true;
            commandGroup.HasMenu = false;
            commandGroup.Activate();

            RemoveLegacyCommandTabs();
        }

        /*
         * This add-in originally created a dedicated CommandManager tab and made
         * it active. SOLIDWORKS persists that active-tab choice between sessions,
         * which makes the add-in appear to be the default workspace tab.
         * The menu/toolbar command group is enough, so keep removing old tabs
         * that earlier development builds may have left behind.
         */
        private void RemoveLegacyCommandTabs()
        {
            RemoveCommandTab((int)swDocumentTypes_e.swDocPART, LegacyAddinTitle);
            RemoveCommandTab((int)swDocumentTypes_e.swDocASSEMBLY, LegacyAddinTitle);
            RemoveCommandTab((int)swDocumentTypes_e.swDocPART, AddinTitle);
            RemoveCommandTab((int)swDocumentTypes_e.swDocASSEMBLY, AddinTitle);
        }

        private void AddCommandTab(int documentType, int exportCommandId)
        {
            CommandTab existingTab = _commandManager.GetCommandTab(documentType, AddinTitle);
            if (existingTab != null)
            {
                existingTab.Visible = true;
                return;
            }

            CommandTab commandTab = _commandManager.AddCommandTab(documentType, AddinTitle);
            if (commandTab == null)
            {
                return;
            }

            CommandTabBox commandTabBox = commandTab.AddCommandTabBox();
            if (commandTabBox == null)
            {
                return;
            }

            int[] commandIds = { exportCommandId };
            int[] textStyles = { (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextHorizontal };
            commandTabBox.AddCommands(commandIds, textStyles);
            commandTab.Visible = true;
        }

        private void AddFallbackMenuItems()
        {
            if (_swApp == null)
            {
                return;
            }

            _swApp.AddMenuItem3(
                (int)swDocumentTypes_e.swDocNONE,
                _addinId,
                "Export to 3D-printer@&Tools",
                -1,
                "ExportBodyToSlicer",
                "AlwaysEnableExportBodyToSlicer",
                "Export a body as STL or STEP and open it in your slicer",
                string.Empty);

            _swApp.AddMenuItem3(
                (int)swDocumentTypes_e.swDocASSEMBLY,
                _addinId,
                "Export to 3D-printer@&Tools",
                -1,
                "ExportBodyToSlicer",
                "AlwaysEnableExportBodyToSlicer",
                "Export a body as STL or STEP and open it in your slicer",
                string.Empty);

            _swApp.AddMenuItem3(
                (int)swDocumentTypes_e.swDocPART,
                _addinId,
                "Export to 3D-printer@&Tools",
                -1,
                "ExportBodyToSlicer",
                "AlwaysEnableExportBodyToSlicer",
                "Export a body as STL or STEP and open it in your slicer",
                string.Empty);
        }

        private static IconFileSet EnsureIconFiles()
        {
            string iconDirectory = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                "icons");

            if (!Directory.Exists(iconDirectory))
            {
                Directory.CreateDirectory(iconDirectory);
            }

            string smallIcon = Path.Combine(iconDirectory, "prototype-exporter-20.bmp");
            string largeIcon = Path.Combine(iconDirectory, "prototype-exporter-32.bmp");
            string sourceIcon = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                "assets",
                "3d-printing-icon.jpg");

            if (File.Exists(sourceIcon))
            {
                CreateIconBitmapFromSource(sourceIcon, smallIcon, 20);
                CreateIconBitmapFromSource(sourceIcon, largeIcon, 32);
            }
            else if (!File.Exists(smallIcon))
            {
                CreateIconBitmap(smallIcon, 20);
            }

            if (!File.Exists(largeIcon))
            {
                CreateIconBitmap(largeIcon, 32);
            }

            return new IconFileSet
            {
                SmallMainIcon = smallIcon,
                LargeMainIcon = largeIcon,
                SmallIconList = smallIcon,
                LargeIconList = largeIcon
            };
        }

        private static void CreateIconBitmap(string path, int size)
        {
            using (var bitmap = new Bitmap(size, size))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (var backgroundBrush = new SolidBrush(Color.FromArgb(30, 132, 214)))
            using (var bodyBrush = new SolidBrush(Color.FromArgb(245, 250, 255)))
            using (var arrowBrush = new SolidBrush(Color.FromArgb(31, 185, 99)))
            using (var pen = new Pen(Color.FromArgb(20, 80, 135), Math.Max(1, size / 16)))
            {
                graphics.Clear(Color.Magenta);
                graphics.FillRectangle(backgroundBrush, 0, 0, size - 1, size - 1);
                graphics.DrawRectangle(pen, 0, 0, size - 1, size - 1);

                int margin = Math.Max(3, size / 6);
                Rectangle bodyRectangle = new Rectangle(margin, margin, size / 2, size / 2);
                graphics.FillRectangle(bodyBrush, bodyRectangle);
                graphics.DrawRectangle(pen, bodyRectangle);

                Point[] arrow =
                {
                    new Point(size - margin - 1, size / 2),
                    new Point(size - margin - 1, size - margin),
                    new Point(size - margin / 2, size - margin),
                    new Point(size / 2, size - 2),
                    new Point(margin, size - margin),
                    new Point(size / 2, size - margin),
                    new Point(size / 2, size / 2)
                };
                graphics.FillPolygon(arrowBrush, arrow);

                bitmap.MakeTransparent(Color.Magenta);
                bitmap.Save(path, ImageFormat.Bmp);
            }
        }

        private static void CreateIconBitmapFromSource(string sourcePath, string outputPath, int size)
        {
            using (Image source = Image.FromFile(sourcePath))
            using (Bitmap output = new Bitmap(size, size))
            using (Graphics graphics = Graphics.FromImage(output))
            {
                graphics.Clear(Color.Magenta);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                Rectangle crop = FindNonWhiteBounds(source);
                int padding = Math.Max(1, size / 10);
                Rectangle target = FitRectangle(crop.Size, new Rectangle(padding, padding, size - padding * 2, size - padding * 2));

                using (Bitmap prepared = CreateTransparentSourceBitmap(source, crop))
                {
                    graphics.DrawImage(prepared, target);
                }

                output.MakeTransparent(Color.Magenta);
                output.Save(outputPath, ImageFormat.Bmp);
            }
        }

        private static Rectangle FindNonWhiteBounds(Image image)
        {
            using (Bitmap bitmap = new Bitmap(image))
            {
                int left = bitmap.Width;
                int top = bitmap.Height;
                int right = 0;
                int bottom = 0;

                for (int y = 0; y < bitmap.Height; y++)
                {
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        Color color = bitmap.GetPixel(x, y);
                        if (color.R < 245 || color.G < 245 || color.B < 245)
                        {
                            left = Math.Min(left, x);
                            top = Math.Min(top, y);
                            right = Math.Max(right, x);
                            bottom = Math.Max(bottom, y);
                        }
                    }
                }

                if (left > right || top > bottom)
                {
                    return new Rectangle(0, 0, bitmap.Width, bitmap.Height);
                }

                return Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
            }
        }

        private static Bitmap CreateTransparentSourceBitmap(Image source, Rectangle crop)
        {
            Bitmap result = new Bitmap(crop.Width, crop.Height);
            using (Graphics graphics = Graphics.FromImage(result))
            {
                graphics.DrawImage(source, new Rectangle(0, 0, crop.Width, crop.Height), crop, GraphicsUnit.Pixel);
            }

            for (int y = 0; y < result.Height; y++)
            {
                for (int x = 0; x < result.Width; x++)
                {
                    Color color = result.GetPixel(x, y);
                    if (color.R > 245 && color.G > 245 && color.B > 245)
                    {
                        result.SetPixel(x, y, Color.Magenta);
                    }
                }
            }

            result.MakeTransparent(Color.Magenta);
            return result;
        }

        private static Rectangle FitRectangle(Size sourceSize, Rectangle bounds)
        {
            double scale = Math.Min((double)bounds.Width / sourceSize.Width, (double)bounds.Height / sourceSize.Height);
            int width = Math.Max(1, (int)Math.Round(sourceSize.Width * scale));
            int height = Math.Max(1, (int)Math.Round(sourceSize.Height * scale));
            int x = bounds.Left + (bounds.Width - width) / 2;
            int y = bounds.Top + (bounds.Height - height) / 2;

            return new Rectangle(x, y, width, height);
        }

        private void RemoveCommandManager()
        {
            if (_commandManager != null)
            {
                RemoveLegacyCommandTabs();

                _commandManager.RemoveCommandGroup(CommandGroupId);
            }
        }

        private void RemoveCommandTab(int documentType)
        {
            RemoveCommandTab(documentType, AddinTitle);
        }

        private void RemoveCommandTab(int documentType, string tabName)
        {
            CommandTab existingTab = _commandManager.GetCommandTab(documentType, tabName);
            if (existingTab != null)
            {
                _commandManager.RemoveCommandTab(existingTab);
            }
        }

        [ComRegisterFunction]
        public static void RegisterFunction(Type type)
        {
            string guid = "{" + type.GUID.ToString().ToUpperInvariant() + "}";

            using (RegistryKey key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\SOLIDWORKS\Addins\" + guid))
            {
                key.SetValue(null, 1, RegistryValueKind.DWord);
                key.SetValue("Title", AddinTitle, RegistryValueKind.String);
                key.SetValue("Description", AddinDescription, RegistryValueKind.String);
            }
        }

        [ComUnregisterFunction]
        public static void UnregisterFunction(Type type)
        {
            string guid = "{" + type.GUID.ToString().ToUpperInvariant() + "}";

            TryDeleteSubKey(Registry.LocalMachine, @"SOFTWARE\SOLIDWORKS\Addins\" + guid);
            TryDeleteSubKey(Registry.CurrentUser, @"Software\SOLIDWORKS\AddInsStartup\" + guid);
        }

        private static void TryDeleteSubKey(RegistryKey root, string keyName)
        {
            try
            {
                root.DeleteSubKey(keyName, false);
            }
            catch
            {
            }
        }

        private static void Log(string message)
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

        private sealed class IconFileSet
        {
            public string SmallMainIcon { get; set; }
            public string LargeMainIcon { get; set; }
            public string SmallIconList { get; set; }
            public string LargeIconList { get; set; }
        }
    }
}
