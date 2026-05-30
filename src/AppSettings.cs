using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace RoundedTask
{
    internal sealed class AppSettings
    {
        public bool Enabled = true;
        public bool ApplyToSecondaryTaskbars = true;
        public bool RestoreOnExit = true;
        public int CornerRadius = 16;
        public int MarginLeft = 8;
        public int MarginTop = 4;
        public int MarginRight = 8;
        public int MarginBottom = 4;
        public int CenterWidth = 560;
        public int CenterOffset = 0;
        public int SystemWidth = 360;
        public int SystemRightInset = 0;
        public int PollIntervalMs = 500;
        public string Preset = "Balanceado";
        public string ShapeMode = "FullBar";
        public bool UseDpiScaling = true;
        public bool ShowSystemTraySegment = true;
        public bool ShowTrayOnHover = false;
        public bool ShowAppsOnHover = false;
        public bool FillOnMaximized = false;
        public bool FillOnTaskSwitcher = false;
        public bool TranslucentTbCompatibility = false;

        public static string SettingsPath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "RoundedTask");
                return Path.Combine(dir, "settings.ini");
            }
        }

        public static AppSettings Load()
        {
            AppSettings settings = new AppSettings();
            string path = SettingsPath;

            if (!File.Exists(path))
            {
                return settings;
            }

            string[] lines = File.ReadAllLines(path);
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                int equals = line.IndexOf('=');
                if (equals <= 0)
                {
                    continue;
                }

                values[line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim();
            }

            settings.Enabled = ReadBool(values, "Enabled", settings.Enabled);
            settings.ApplyToSecondaryTaskbars = ReadBool(values, "ApplyToSecondaryTaskbars", settings.ApplyToSecondaryTaskbars);
            settings.RestoreOnExit = ReadBool(values, "RestoreOnExit", settings.RestoreOnExit);
            settings.CornerRadius = ReadInt(values, "CornerRadius", settings.CornerRadius, 0, 96);
            settings.MarginLeft = ReadInt(values, "MarginLeft", settings.MarginLeft, 0, 1500);
            settings.MarginTop = ReadInt(values, "MarginTop", settings.MarginTop, 0, 256);
            settings.MarginRight = ReadInt(values, "MarginRight", settings.MarginRight, 0, 1500);
            settings.MarginBottom = ReadInt(values, "MarginBottom", settings.MarginBottom, 0, 256);
            settings.CenterWidth = ReadInt(values, "CenterWidth", settings.CenterWidth, 80, 3000);
            settings.CenterOffset = ReadInt(values, "CenterOffset", settings.CenterOffset, -1500, 1500);
            settings.SystemWidth = ReadInt(values, "SystemWidth", settings.SystemWidth, 80, 1200);
            settings.SystemRightInset = ReadInt(values, "SystemRightInset", settings.SystemRightInset, 0, 600);
            settings.PollIntervalMs = ReadInt(values, "PollIntervalMs", settings.PollIntervalMs, 150, 10000);
            settings.UseDpiScaling = ReadBool(values, "UseDpiScaling", settings.UseDpiScaling);
            settings.ShowSystemTraySegment = ReadBool(values, "ShowSystemTraySegment", settings.ShowSystemTraySegment);
            settings.ShowTrayOnHover = ReadBool(values, "ShowTrayOnHover", settings.ShowTrayOnHover);
            settings.ShowAppsOnHover = ReadBool(values, "ShowAppsOnHover", settings.ShowAppsOnHover);
            settings.FillOnMaximized = ReadBool(values, "FillOnMaximized", settings.FillOnMaximized);
            settings.FillOnTaskSwitcher = ReadBool(values, "FillOnTaskSwitcher", settings.FillOnTaskSwitcher);
            settings.TranslucentTbCompatibility = ReadBool(values, "TranslucentTbCompatibility", settings.TranslucentTbCompatibility);

            string preset;
            if (values.TryGetValue("Preset", out preset) && preset.Length > 0)
            {
                settings.Preset = NormalizePreset(preset);
            }

            string shapeMode;
            if (values.TryGetValue("ShapeMode", out shapeMode) && shapeMode.Length > 0)
            {
                settings.ShapeMode = NormalizeShapeMode(shapeMode);
            }

            return settings;
        }

        public void Save()
        {
            string path = SettingsPath;
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string[] lines = new string[]
            {
                "Enabled=" + Enabled.ToString(CultureInfo.InvariantCulture),
                "ApplyToSecondaryTaskbars=" + ApplyToSecondaryTaskbars.ToString(CultureInfo.InvariantCulture),
                "RestoreOnExit=" + RestoreOnExit.ToString(CultureInfo.InvariantCulture),
                "CornerRadius=" + CornerRadius.ToString(CultureInfo.InvariantCulture),
                "MarginLeft=" + MarginLeft.ToString(CultureInfo.InvariantCulture),
                "MarginTop=" + MarginTop.ToString(CultureInfo.InvariantCulture),
                "MarginRight=" + MarginRight.ToString(CultureInfo.InvariantCulture),
                "MarginBottom=" + MarginBottom.ToString(CultureInfo.InvariantCulture),
                "CenterWidth=" + CenterWidth.ToString(CultureInfo.InvariantCulture),
                "CenterOffset=" + CenterOffset.ToString(CultureInfo.InvariantCulture),
                "SystemWidth=" + SystemWidth.ToString(CultureInfo.InvariantCulture),
                "SystemRightInset=" + SystemRightInset.ToString(CultureInfo.InvariantCulture),
                "PollIntervalMs=" + PollIntervalMs.ToString(CultureInfo.InvariantCulture),
                "UseDpiScaling=" + UseDpiScaling.ToString(CultureInfo.InvariantCulture),
                "ShowSystemTraySegment=" + ShowSystemTraySegment.ToString(CultureInfo.InvariantCulture),
                "ShowTrayOnHover=" + ShowTrayOnHover.ToString(CultureInfo.InvariantCulture),
                "ShowAppsOnHover=" + ShowAppsOnHover.ToString(CultureInfo.InvariantCulture),
                "FillOnMaximized=" + FillOnMaximized.ToString(CultureInfo.InvariantCulture),
                "FillOnTaskSwitcher=" + FillOnTaskSwitcher.ToString(CultureInfo.InvariantCulture),
                "TranslucentTbCompatibility=" + TranslucentTbCompatibility.ToString(CultureInfo.InvariantCulture),
                "ShapeMode=" + ShapeMode,
                "Preset=" + Preset
            };

            File.WriteAllLines(path, lines);
        }

        public AppSettings Clone()
        {
            return (AppSettings)MemberwiseClone();
        }

        public void ApplyPreset(string preset)
        {
            Preset = NormalizePreset(preset);

            if (String.Equals(Preset, "Compacto", StringComparison.OrdinalIgnoreCase))
            {
                ShapeMode = "FullBar";
                CornerRadius = 12;
                MarginLeft = 4;
                MarginTop = 2;
                MarginRight = 4;
                MarginBottom = 2;
            }
            else if (String.Equals(Preset, "Flutuante", StringComparison.OrdinalIgnoreCase))
            {
                ShapeMode = "FullBar";
                CornerRadius = 20;
                MarginLeft = 18;
                MarginTop = 6;
                MarginRight = 18;
                MarginBottom = 8;
            }
            else if (String.Equals(Preset, "P\u00edlula completa", StringComparison.OrdinalIgnoreCase))
            {
                ShapeMode = "FullBar";
                CornerRadius = 48;
                MarginLeft = 24;
                MarginTop = 5;
                MarginRight = 24;
                MarginBottom = 7;
            }
            else if (String.Equals(Preset, "Centro e sistema", StringComparison.OrdinalIgnoreCase))
            {
                ShapeMode = "CenterAndSystem";
                CornerRadius = 24;
                MarginLeft = 0;
                MarginTop = 5;
                MarginRight = 10;
                MarginBottom = 7;
                CenterWidth = 560;
                CenterOffset = 0;
                SystemWidth = 360;
                SystemRightInset = 0;
            }
            else if (String.Equals(Preset, "Segmentos autom\u00e1ticos", StringComparison.OrdinalIgnoreCase))
            {
                ShapeMode = "DynamicSegments";
                CornerRadius = 18;
                MarginLeft = 8;
                MarginTop = 4;
                MarginRight = 8;
                MarginBottom = 5;
                ShowSystemTraySegment = true;
                ShowTrayOnHover = false;
                ShowAppsOnHover = false;
                FillOnMaximized = false;
                FillOnTaskSwitcher = false;
            }
            else
            {
                Preset = "Balanceado";
                ShapeMode = "FullBar";
                CornerRadius = 16;
                MarginLeft = 8;
                MarginTop = 4;
                MarginRight = 8;
                MarginBottom = 4;
            }
        }

        public static string NormalizePreset(string preset)
        {
            if (String.Equals(preset, "Balanced", StringComparison.OrdinalIgnoreCase))
            {
                return "Balanceado";
            }

            if (String.Equals(preset, "Compact", StringComparison.OrdinalIgnoreCase))
            {
                return "Compacto";
            }

            if (String.Equals(preset, "Floating", StringComparison.OrdinalIgnoreCase))
            {
                return "Flutuante";
            }

            if (String.Equals(preset, "Full pill", StringComparison.OrdinalIgnoreCase))
            {
                return "P\u00edlula completa";
            }

            if (String.Equals(preset, "Compacto", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(preset, "Flutuante", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(preset, "Pilula completa", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(preset, "P\u00edlula completa", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(preset, "Centro e sistema", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(preset, "Segmentos automaticos", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(preset, "Segmentos autom\u00e1ticos", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(preset, "Personalizado", StringComparison.OrdinalIgnoreCase))
            {
                if (String.Equals(preset, "Pilula completa", StringComparison.OrdinalIgnoreCase))
                {
                    return "P\u00edlula completa";
                }

                if (String.Equals(preset, "Segmentos automaticos", StringComparison.OrdinalIgnoreCase))
                {
                    return "Segmentos autom\u00e1ticos";
                }

                return preset;
            }

            return "Balanceado";
        }

        public static string NormalizeShapeMode(string mode)
        {
            if (String.Equals(mode, "CenterAndSystem", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(mode, "Centro + sistema", StringComparison.OrdinalIgnoreCase))
            {
                return "CenterAndSystem";
            }

            if (String.Equals(mode, "DynamicSegments", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(mode, "Segmentos autom\u00e1ticos", StringComparison.OrdinalIgnoreCase))
            {
                return "DynamicSegments";
            }

            return "FullBar";
        }

        private static bool ReadBool(Dictionary<string, string> values, string key, bool fallback)
        {
            string value;
            bool result;
            if (values.TryGetValue(key, out value) && Boolean.TryParse(value, out result))
            {
                return result;
            }

            return fallback;
        }

        private static int ReadInt(Dictionary<string, string> values, string key, int fallback, int min, int max)
        {
            string value;
            int result;
            if (values.TryGetValue(key, out value) && Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result))
            {
                if (result < min)
                {
                    return min;
                }

                if (result > max)
                {
                    return max;
                }

                return result;
            }

            return fallback;
        }
    }
}
