using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using GothicSaveEditor.Core.Services;

namespace GothicSaveEditor.Core.Utils
{
    public static class Options
    {
        #region Parameters
        private static string? _language;
        public static string Language
        {
            get => _language ?? "English";
            set
            {
                if (_language != value)
                {
                    _language = value;
                    Save();
                }
            }
        }

        private static string _gamePath = "";
        public static string GamePath
        {
            get => _gamePath;
            set
            {
                _gamePath = value ?? "";
                Save();
            }
        }

        private static bool _keepBackups = true;
        public static bool KeepBackups
        {
            get => _keepBackups;
            set
            {
                _keepBackups = value;
                Save();
            }
        }

        private static int _codePage = 1252;
        public static int CodePage
        {
            get => _codePage;
            set
            {
                _codePage = value;
                Save();
            }
        }

        private static bool _darkMode = true;
        public static bool DarkMode
        {
            get => _darkMode;
            set
            {
                _darkMode = value;
                Save();
            }
        }

        private static int _maxBackups = 10;
        public static int MaxBackups
        {
            get => _maxBackups;
            set
            {
                _maxBackups = Math.Clamp(value, 1, 100);
                BackupService.MaxBackupsPerSlot = _maxBackups;
                Save();
            }
        }

        private static bool _associateSavFiles;
        public static bool AssociateSavFiles
        {
            get => _associateSavFiles;
            set
            {
                _associateSavFiles = value;
                Save();
            }
        }
        #endregion

        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        #region App main settings
        public static int InfoLinePopUpTime = 1500;

        public static readonly string ScriptsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts");

        public static string GseVersion =>
            "GothicSaveEditor Redux v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0");

        private static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GothicSaveEditor");

        private static readonly string SettingsFile = Path.Combine(AppDataFolder, "settings.gsec");
        private static readonly string LegacySettingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.gsec");
        #endregion

        static Options()
        {
            var loadState = Load();
            BackupService.MaxBackupsPerSlot = _maxBackups;

            if (_language == null)
            {
                string uiLang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                if (uiLang == "de")
                    App.SetLanguage("Deutsch");
                else if (uiLang == "ru")
                    App.SetLanguage("Русский");
                else
                    App.SetLanguage("English");
            }
            else
            {
                App.SetLanguage(_language);
            }

            if (!loadState)
                MessageBox.Show(ResourceServices.GetString("UnableToLoadSettings"));
        }

        private static readonly Dictionary<string, Action<string>> Settings = new()
        {
            ["language"] = val =>
            {
                if (val == "Russian")
                    _language = "Русский";
                else if (val == "German" || val == "Deutsch")
                    _language = "Deutsch";
                else if (val == "English")
                    _language = "English";
            },
            ["game_path"] = val => _gamePath = val,
            ["keep_backups"] = val =>
            {
                if (bool.TryParse(val, out bool parsed))
                    _keepBackups = parsed;
            },
            ["code_page"] = val =>
            {
                if (int.TryParse(val, out int cp) && (cp == 1250 || cp == 1251 || cp == 1252))
                    _codePage = cp;
            },
            ["dark_mode"] = val =>
            {
                if (bool.TryParse(val, out bool dark))
                    _darkMode = dark;
            },
            ["max_backups"] = val =>
            {
                if (int.TryParse(val, out int max))
                    _maxBackups = Math.Clamp(max, 1, 100);
            },
            ["associate_sav"] = val =>
            {
                if (bool.TryParse(val, out bool assoc))
                    _associateSavFiles = assoc;
            }
        };

        public static bool Load()
        {
            string? fileToLoad = File.Exists(SettingsFile)
                ? SettingsFile
                : File.Exists(LegacySettingsFile) ? LegacySettingsFile : null;

            if (fileToLoad == null)
                return true;

            try
            {
                foreach (var line in File.ReadAllLines(fileToLoad))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    int eqIdx = line.IndexOf('=');
                    if (eqIdx <= 0) continue;

                    string key = line[..eqIdx].Trim().ToLowerInvariant();
                    string val = line[(eqIdx + 1)..].Trim();
                    if (Settings.TryGetValue(key, out var handler))
                        handler(val);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                return false;
            }

            return true;
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(AppDataFolder);

                var sb = new StringBuilder();
                if (_language != null)
                {
                    string serializedLang = _language switch
                    {
                        "Русский" => "Russian",
                        "Deutsch" => "German",
                        _ => _language
                    };
                    sb.AppendLine($"language={serializedLang}");
                }

                if (_gamePath != null)
                    sb.AppendLine($"game_path={_gamePath}");

                sb.AppendLine($"keep_backups={_keepBackups}");
                sb.AppendLine($"code_page={_codePage}");
                sb.AppendLine($"dark_mode={_darkMode}");
                sb.AppendLine($"max_backups={_maxBackups}");
                sb.AppendLine($"associate_sav={_associateSavFiles}");

                File.WriteAllText(SettingsFile, sb.ToString());
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                MessageBox.Show(ResourceServices.GetString("UnableToSaveSettings"));
            }
        }
    }
}
