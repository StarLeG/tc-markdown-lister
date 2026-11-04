// Автор: Старинский Олег Григорьевич (info@electroman.by, @ElectroManBY)
// Ядро MarkdownLister: настройки плагина (INI-файл рядом с плагином).

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

namespace MarkdownLister.Core
{
    /// <summary>Настройки плагина. Хранятся в INI-файле рядом с плагином, без телеметрии.</summary>
    public sealed class PluginSettings
    {
        /// <summary>Имя INI-файла.</summary>
        public const string IniFileName = "MarkdownLister.ini";

        /// <summary>Имя JSON-пресета темы из папки themes\ рядом с плагином (без расширения); пусто — без пресета.</summary>
        public string ThemePreset { get; set; } = "";

        /// <summary>Язык интерфейса: Auto (по языку ОС), Ru, En.</summary>
        public string Language { get; set; } = "Auto";
        /// <summary>Тема оформления: System, Light, Dark.</summary>
        public string Theme { get; set; } = "System";
        /// <summary>Тема подсветки кода (имя CSS-файла Prism/highlight.js).</summary>
        public string CodeTheme { get; set; } = "github-dark";
        /// <summary>Масштаб отображения, % (50–200).</summary>
        public int Zoom { get; set; } = 100;
        /// <summary>Ширина колонки чтения, px (600–2000).</summary>
        public int ReadingWidth { get; set; } = 900;
        /// <summary>Показывать номера строк в блоках кода.</summary>
        public bool ShowLineNumbers { get; set; } = true;
        /// <summary>Показывать оглавление (TOC) в боковой панели.</summary>
        public bool ShowToc { get; set; } = true;
        /// <summary>Режим split view (исходник рядом с рендерингом).</summary>
        public bool SplitView { get; set; } = false;
        /// <summary>Логировать ошибки в файл.</summary>
        public bool LogErrors { get; set; } = true;
        /// <summary>Предупреждать о файлах больше этого размера, МБ.</summary>
        public int LargeFileWarningMb { get; set; } = 5;

        /// <summary>Путь к INI-файлу.</summary>
        public string IniPath { get; }

        /// <summary>Загружает настройки из INI-файла рядом с указанным файлом плагина.</summary>
        /// <param name="pluginPath">Путь к .wlx (или любому файлу в папке плагина).</param>
        public PluginSettings(string pluginPath)
        {
            var dir = Path.GetDirectoryName(pluginPath) ?? AppDomain.CurrentDomain.BaseDirectory;
            IniPath = Path.Combine(dir, IniFileName);
            Load();
        }

        /// <summary>
        /// Читает настройки: значения по умолчанию → JSON-пресет темы (themes\*.json) → INI-файл.
        /// Значения из INI персональнее и потому побеждают пресет.
        /// </summary>
        public void Load()
        {
            // Выбор пресета хранится в INI; читаем его даже при отсутствии файла (будет fallback).
            ThemePreset = Read("View", "ThemePreset", ThemePreset);
            ApplyThemePreset();

            if (!File.Exists(IniPath))
                return;

            Language = Read("View", "Language", Language);
            Theme = Read("View", "Theme", Theme);
            CodeTheme = Read("View", "CodeTheme", CodeTheme);
            Zoom = Clamp(ReadInt("View", "Zoom", Zoom), 50, 200);
            ReadingWidth = Clamp(ReadInt("View", "ReadingWidth", ReadingWidth), 600, 2000);
            ShowLineNumbers = ReadBool("View", "ShowLineNumbers", ShowLineNumbers);
            ShowToc = ReadBool("View", "ShowToc", ShowToc);
            SplitView = ReadBool("View", "SplitView", SplitView);
            LogErrors = ReadBool("General", "LogErrors", LogErrors);
            LargeFileWarningMb = Clamp(ReadInt("General", "LargeFileWarningMb", LargeFileWarningMb), 1, 500);
        }

        /// <summary>Сохраняет настройки в INI-файл.</summary>
        public void Save()
        {
            Write("View", "Language", Language);
            Write("View", "ThemePreset", ThemePreset ?? "");
            Write("View", "Theme", Theme);
            Write("View", "CodeTheme", CodeTheme);
            Write("View", "Zoom", Zoom.ToString());
            Write("View", "ReadingWidth", ReadingWidth.ToString());
            Write("View", "ShowLineNumbers", ShowLineNumbers ? "1" : "0");
            Write("View", "ShowToc", ShowToc ? "1" : "0");
            Write("View", "SplitView", SplitView ? "1" : "0");
            Write("General", "LogErrors", LogErrors ? "1" : "0");
            Write("General", "LargeFileWarningMb", LargeFileWarningMb.ToString());
        }

        /// <summary>Путь к папке тем рядом с плагином (themes\).</summary>
        public string ThemesDirectory
            => Path.Combine(Path.GetDirectoryName(IniPath) ?? AppDomain.CurrentDomain.BaseDirectory, "themes");

        /// <summary>
        /// Применяет JSON-пресет темы из themes\&lt;ThemePreset&gt;.json поверх текущих значений.
        /// Повреждённый или отсутствующий пресет молча игнорируется — плагин не должен падать.
        /// Поддерживаемые поля: theme, codeTheme, zoom, readingWidth, showLineNumbers, showToc, splitView.
        /// </summary>
        public void ApplyThemePreset()
        {
            if (string.IsNullOrWhiteSpace(ThemePreset))
                return;

            string path = Path.Combine(ThemesDirectory, ThemePreset.Trim() + ".json");
            if (!File.Exists(path))
                return;

            try
            {
                var serializer = new JavaScriptSerializer();
                var data = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
                if (data == null)
                    return;

                if (TryString(data, "theme", out var theme)) Theme = theme;
                if (TryString(data, "codeTheme", out var codeTheme)) CodeTheme = codeTheme;
                if (TryInt(data, "zoom", out var zoom)) Zoom = Clamp(zoom, 50, 200);
                if (TryInt(data, "readingWidth", out var width)) ReadingWidth = Clamp(width, 600, 2000);
                if (TryBool(data, "showLineNumbers", out var lineNumbers)) ShowLineNumbers = lineNumbers;
                if (TryBool(data, "showToc", out var toc)) ShowToc = toc;
                if (TryBool(data, "splitView", out var split)) SplitView = split;
            }
            catch
            {
                // Пресет — пользовательский файл: ошибка разбора не должна ломать просмотр.
            }
        }

        private static bool TryString(Dictionary<string, object> data, string key, out string value)
        {
            value = null;
            if (!data.TryGetValue(key, out var raw) || !(raw is string s) || string.IsNullOrWhiteSpace(s))
                return false;
            value = s.Trim();
            return true;
        }

        private static bool TryInt(Dictionary<string, object> data, string key, out int value)
        {
            value = 0;
            if (!data.TryGetValue(key, out var raw))
                return false;
            try
            {
                value = Convert.ToInt32(raw);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryBool(Dictionary<string, object> data, string key, out bool value)
        {
            value = false;
            if (!data.TryGetValue(key, out var raw))
                return false;
            if (raw is bool b) { value = b; return true; }
            var s = raw as string;
            if (s == "1" || (s != null && s.Equals("true", StringComparison.OrdinalIgnoreCase))) { value = true; return true; }
            return false;
        }

        private static int Clamp(int value, int min, int max)
            => value < min ? min : (value > max ? max : value);

        private string Read(string section, string key, string fallback)
        {
            var sb = new StringBuilder(256);
            GetPrivateProfileString(section, key, fallback, sb, sb.Capacity, IniPath);
            return sb.ToString();
        }

        private int ReadInt(string section, string key, int fallback)
        {
            var s = Read(section, key, fallback.ToString());
            return int.TryParse(s, out var v) ? v : fallback;
        }

        private bool ReadBool(string section, string key, bool fallback)
        {
            var s = Read(section, key, fallback ? "1" : "0");
            return s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        private void Write(string section, string key, string value)
            => WritePrivateProfileString(section, key, value, IniPath);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetPrivateProfileString(
            string section, string key, string fallback, StringBuilder retVal, int size, string filePath);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool WritePrivateProfileString(
            string section, string key, string value, string filePath);
    }
}
