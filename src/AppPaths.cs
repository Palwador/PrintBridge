using System;
using System.IO;

namespace SwPrototypeExporter
{
    internal static class AppPaths
    {
        public const string AddinName = "PrintBridge";

        public static string UserDataDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    AddinName);
            }
        }

        public static string IconDirectory
        {
            get { return Path.Combine(UserDataDirectory, "icons"); }
        }

        public static string SettingsPath
        {
            get { return Path.Combine(UserDataDirectory, "settings.ini"); }
        }

        public static string LogPath
        {
            get { return Path.Combine(UserDataDirectory, "addin.log"); }
        }

        public static string TemporaryExportDirectory
        {
            get { return Path.Combine(UserDataDirectory, "TemporaryExports"); }
        }
    }
}
