using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace SwPrototypeExporter
{
    internal sealed class SlicerChoice
    {
        public SlicerChoice(string name, string executablePath)
        {
            Name = name;
            ExecutablePath = executablePath;
        }

        public string Name { get; private set; }
        public string ExecutablePath { get; private set; }

        public override string ToString()
        {
            return Name;
        }
    }

    internal static class SlicerDiscovery
    {
        public static List<SlicerChoice> FindInstalledSlicers(string preferredPath)
        {
            var slicers = new List<SlicerChoice>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddIfExists(slicers, seenPaths, "PrusaSlicer", @"C:\Program Files\Prusa3D\PrusaSlicer\prusa-slicer.exe");
            AddIfExists(slicers, seenPaths, "Bambu Studio", @"C:\Program Files\Bambu Studio\bambu-studio.exe");
            AddIfExists(slicers, seenPaths, "OrcaSlicer", @"C:\Program Files\OrcaSlicer\orca-slicer.exe");
            AddIfExists(slicers, seenPaths, "OrcaSlicer", @"C:\Program Files\OrcaSlicer\OrcaSlicer.exe");
            AddIfExists(slicers, seenPaths, "SuperSlicer", @"C:\Program Files\SuperSlicer\superslicer.exe");

            ScanProgramFilesForCura(slicers, seenPaths, System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles));
            ScanProgramFilesForCura(slicers, seenPaths, System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFilesX86));
            ScanUninstallRegistry(slicers, seenPaths, Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            ScanUninstallRegistry(slicers, seenPaths, Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall");
            ScanUninstallRegistry(slicers, seenPaths, Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");

            if (!string.IsNullOrWhiteSpace(preferredPath) && File.Exists(preferredPath))
            {
                AddIfExists(slicers, seenPaths, "Saved slicer", preferredPath);
                MovePreferredToTop(slicers, preferredPath);
            }

            return slicers;
        }

        private static void ScanProgramFilesForCura(List<SlicerChoice> slicers, HashSet<string> seenPaths, string programFiles)
        {
            if (string.IsNullOrWhiteSpace(programFiles) || !Directory.Exists(programFiles))
            {
                return;
            }

            try
            {
                foreach (string directory in Directory.EnumerateDirectories(programFiles, "*Cura*"))
                {
                    string displayName = Path.GetFileName(directory);
                    AddIfExists(slicers, seenPaths, displayName, Path.Combine(directory, "UltiMaker-Cura.exe"));
                    AddIfExists(slicers, seenPaths, displayName, Path.Combine(directory, "Cura.exe"));
                }
            }
            catch
            {
            }
        }

        private static void ScanUninstallRegistry(List<SlicerChoice> slicers, HashSet<string> seenPaths, RegistryKey root, string keyPath)
        {
            try
            {
                using (RegistryKey uninstallKey = root.OpenSubKey(keyPath))
                {
                    if (uninstallKey == null)
                    {
                        return;
                    }

                    foreach (string subKeyName in uninstallKey.GetSubKeyNames())
                    {
                        using (RegistryKey appKey = uninstallKey.OpenSubKey(subKeyName))
                        {
                            if (appKey == null)
                            {
                                continue;
                            }

                            string displayName = Convert.ToString(appKey.GetValue("DisplayName"));
                            if (!LooksLikeSlicer(displayName))
                            {
                                continue;
                            }

                            string installLocation = Convert.ToString(appKey.GetValue("InstallLocation"));
                            string displayIcon = CleanDisplayIcon(Convert.ToString(appKey.GetValue("DisplayIcon")));

                            AddIfExists(slicers, seenPaths, displayName, displayIcon);
                            AddKnownExecutableNames(slicers, seenPaths, displayName, installLocation);
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static bool LooksLikeSlicer(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return false;
            }

            return displayName.IndexOf("PrusaSlicer", StringComparison.OrdinalIgnoreCase) >= 0
                || displayName.IndexOf("Prusa Slicer", StringComparison.OrdinalIgnoreCase) >= 0
                || displayName.IndexOf("Bambu Studio", StringComparison.OrdinalIgnoreCase) >= 0
                || displayName.IndexOf("OrcaSlicer", StringComparison.OrdinalIgnoreCase) >= 0
                || displayName.IndexOf("Orca Slicer", StringComparison.OrdinalIgnoreCase) >= 0
                || displayName.IndexOf("SuperSlicer", StringComparison.OrdinalIgnoreCase) >= 0
                || displayName.IndexOf("Super Slicer", StringComparison.OrdinalIgnoreCase) >= 0
                || displayName.IndexOf("Cura", StringComparison.OrdinalIgnoreCase) >= 0
                || displayName.IndexOf("Lychee", StringComparison.OrdinalIgnoreCase) >= 0
                || displayName.IndexOf("CHITUBOX", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void AddKnownExecutableNames(List<SlicerChoice> slicers, HashSet<string> seenPaths, string displayName, string installLocation)
        {
            if (string.IsNullOrWhiteSpace(installLocation) || !Directory.Exists(installLocation))
            {
                return;
            }

            AddIfExists(slicers, seenPaths, displayName, Path.Combine(installLocation, "prusa-slicer.exe"));
            AddIfExists(slicers, seenPaths, displayName, Path.Combine(installLocation, "bambu-studio.exe"));
            AddIfExists(slicers, seenPaths, displayName, Path.Combine(installLocation, "orca-slicer.exe"));
            AddIfExists(slicers, seenPaths, displayName, Path.Combine(installLocation, "OrcaSlicer.exe"));
            AddIfExists(slicers, seenPaths, displayName, Path.Combine(installLocation, "superslicer.exe"));
            AddIfExists(slicers, seenPaths, displayName, Path.Combine(installLocation, "UltiMaker-Cura.exe"));
            AddIfExists(slicers, seenPaths, displayName, Path.Combine(installLocation, "Cura.exe"));
            AddIfExists(slicers, seenPaths, displayName, Path.Combine(installLocation, "LycheeSlicer.exe"));
            AddIfExists(slicers, seenPaths, displayName, Path.Combine(installLocation, "CHITUBOX.exe"));
        }

        private static string CleanDisplayIcon(string displayIcon)
        {
            if (string.IsNullOrWhiteSpace(displayIcon))
            {
                return string.Empty;
            }

            string cleaned = displayIcon.Trim().Trim('"');
            int commaIndex = cleaned.LastIndexOf(',');
            if (commaIndex > 2 && cleaned.Substring(commaIndex + 1).Trim().TrimStart('-').Length <= 3)
            {
                cleaned = cleaned.Substring(0, commaIndex).Trim().Trim('"');
            }

            return cleaned;
        }

        private static void AddIfExists(List<SlicerChoice> slicers, HashSet<string> seenPaths, string name, string executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                return;
            }

            string fullPath = Path.GetFullPath(executablePath);
            if (!seenPaths.Add(fullPath))
            {
                return;
            }

            slicers.Add(new SlicerChoice(string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(fullPath) : name, fullPath));
        }

        private static void MovePreferredToTop(List<SlicerChoice> slicers, string preferredPath)
        {
            for (int i = 0; i < slicers.Count; i++)
            {
                if (string.Equals(slicers[i].ExecutablePath, preferredPath, StringComparison.OrdinalIgnoreCase))
                {
                    SlicerChoice preferred = slicers[i];
                    slicers.RemoveAt(i);
                    slicers.Insert(0, preferred);
                    return;
                }
            }
        }
    }
}
