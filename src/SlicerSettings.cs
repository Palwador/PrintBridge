using System;
using System.Collections.Generic;
using System.IO;

namespace SwPrototypeExporter
{
    internal sealed class SlicerSettings
    {
        public string ExportDirectory { get; set; }
        public string SlicerPath { get; set; }
        public string LastFormat { get; set; }
        public bool LaunchSlicer { get; set; }
        public bool UseTemporaryFile { get; set; }

        public static SlicerSettings Load()
        {
            var settings = new SlicerSettings
            {
                LastFormat = ExportFormat.Stl.ToString(),
                LaunchSlicer = true
            };

            string path = SettingsPath;
            if (!File.Exists(path))
            {
                return settings;
            }

            foreach (string line in File.ReadAllLines(path))
            {
                int separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, separator);
                string value = line.Substring(separator + 1);

                if (string.Equals(key, "ExportDirectory", StringComparison.OrdinalIgnoreCase))
                {
                    settings.ExportDirectory = value;
                }
                else if (string.Equals(key, "SlicerPath", StringComparison.OrdinalIgnoreCase))
                {
                    settings.SlicerPath = value;
                }
                else if (string.Equals(key, "LastFormat", StringComparison.OrdinalIgnoreCase))
                {
                    settings.LastFormat = value;
                }
                else if (string.Equals(key, "LaunchSlicer", StringComparison.OrdinalIgnoreCase))
                {
                    bool launchSlicer;
                    if (bool.TryParse(value, out launchSlicer))
                    {
                        settings.LaunchSlicer = launchSlicer;
                    }
                }
                else if (string.Equals(key, "UseTemporaryFile", StringComparison.OrdinalIgnoreCase))
                {
                    bool useTemporaryFile;
                    if (bool.TryParse(value, out useTemporaryFile))
                    {
                        settings.UseTemporaryFile = useTemporaryFile;
                    }
                }
            }

            return settings;
        }

        public void Save()
        {
            string directory = Path.GetDirectoryName(SettingsPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllLines(SettingsPath, new[]
            {
                "ExportDirectory=" + (ExportDirectory ?? string.Empty),
                "SlicerPath=" + (SlicerPath ?? string.Empty),
                "LastFormat=" + (LastFormat ?? ExportFormat.Stl.ToString()),
                "LaunchSlicer=" + LaunchSlicer,
                "UseTemporaryFile=" + UseTemporaryFile
            });
        }

        private static string SettingsPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SwPrototypeExporter",
                    "settings.ini");
            }
        }
    }
}
