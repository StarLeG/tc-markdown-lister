// Автор: Старинский Олег Григорьевич (info@electroman.by, @ElectroManBY)
// WLX-плагин MarkdownLister: логирование ошибок (отключаемое настройкой).

using System;
using System.IO;

namespace MarkdownLister.WLX
{
    /// <summary>Простое логирование ошибок в файл рядом с плагином.</summary>
    internal static class Logger
    {
        private static string _logPath;
        private static bool _enabled = true;

        /// <summary>Инициализирует логгер.</summary>
        /// <param name="pluginDir">Папка плагина.</param>
        /// <param name="enabled">Логировать ли ошибки.</param>
        public static void Init(string pluginDir, bool enabled)
        {
            _enabled = enabled;
            try
            {
                _logPath = Path.Combine(pluginDir ?? ".", "MarkdownLister.log");
            }
            catch
            {
                _enabled = false;
            }
        }

        /// <summary>Записывает сообщение об ошибке в лог.</summary>
        public static void Error(string context, Exception ex)
        {
            if (!_enabled || _logPath == null)
                return;
            try
            {
                File.AppendAllText(_logPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}: {ex}{Environment.NewLine}");
            }
            catch
            {
                // Недоступность лога не должна влиять на работу плагина.
            }
        }

        /// <summary>Записывает произвольное сообщение в лог.</summary>
        public static void Info(string message)
        {
            if (!_enabled || _logPath == null)
                return;
            try
            {
                File.AppendAllText(_logPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
            catch
            {
            }
        }
    }
}
