// Автор: Старинский Олег Григорьевич (info@electroman.by, @ElectroManBY)
// WLX-плагин MarkdownLister: точка входа Lister (Total Commander).
// Экспорт функций — [DllExport] (3F, v1.8.1), соглашение о вызовах — StdCall (WLX SDK).

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using MarkdownLister.Core;

// Атрибут [DllExport] поставляется raw-библиотекой пакета DllExport 1.8.1;
// пространство имён обфусцировано производителем (см. MarkdownLister.WLX.csproj,
// свойство DllExportNamespace — оно должно совпадать с этим псевдонимом).
using DllExport = D3F00FF1770DED978EC774BA389F2DC901F4.B00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000.C00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000.D00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000.E00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000.F00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000.DllExportAttribute;

namespace MarkdownLister.WLX
{
    /// <summary>
    /// Экспортируемые функции WLX-протокола Total Commander.
    /// Все вызовы приходят в UI-потоке TC; ошибки не должны «ронять» Lister.
    /// </summary>
    public static class Plugin
    {
        /// <summary>Папка плагина (каталог загруженного .wlx).</summary>
        internal static string PluginDir { get; private set; }

        /// <summary>Настройки плагина (INI рядом с плагином).</summary>
        internal static PluginSettings Settings { get; private set; }

        private static readonly object Sync = new object();
        private static readonly Dictionary<IntPtr, ListerControl> Windows =
            new Dictionary<IntPtr, ListerControl>();

        private static readonly string[] MarkdownExtensions =
        {
            ".md", ".markdown", ".mkd", ".mkdn", ".mdown", ".mdtext",
        };

        static Plugin()
        {
            try
            {
                // Резолв зависимостей и папка плагина — см. Bootstrap (module initializer):
                // AssemblyResolve должен быть подключён ДО JIT-резолва MarkdownLister.Core.
                PluginDir = Bootstrap.PluginDir;

                Settings = new PluginSettings(Path.Combine(PluginDir, "MarkdownLister.WLX.dll"));
                Loc.SetLanguage(Settings.Language); // строки UI — до первого MessageBox
                Logger.Init(PluginDir, Settings.LogErrors);
                Logger.Info("MarkdownLister инициализирован; папка: " + PluginDir);
            }
            catch (Exception ex)
            {
                // Статический конструктор не должен ронять Lister.
                Logger.Error("Plugin..cctor", ex);
            }
        }

        /// <summary>Определяет, является ли файл Markdown по расширению.</summary>
        private static bool IsMarkdown(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;
            string ext = Path.GetExtension(path) ?? string.Empty;
            foreach (string known in MarkdownExtensions)
                if (string.Equals(ext, known, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        // ---------------------------------------------------------------
        // WLX API
        // ---------------------------------------------------------------

        /// <summary>Создаёт окно просмотра для файла (F3 / Quick View).</summary>
        /// <param name="parentWin">Родительское окно Lister.</param>
        /// <param name="fileToLoad">Путь к файлу.</param>
        /// <param name="showFlags">Флаги режима показа (lcp_*).</param>
        /// <returns>HWND окна плагина или NULL при отказе.</returns>
        [DllExport("ListLoad", CallingConvention.StdCall)]
        public static IntPtr ListLoad(IntPtr parentWin,
            [MarshalAs(UnmanagedType.LPStr)] string fileToLoad, int showFlags)
        {
            // Реализация — в ListLoadCore: DllExport дописывает modopt(CallConvStdcall)
            // к сигнатурам экспортируемых методов, поэтому прямой вызов ListLoad из
            // ListLoadW не резолвится (MissingMethodException и падение Total Commander).
            return ListLoadCore(parentWin, fileToLoad);
        }

        /// <summary>Общая реализация ListLoad/ListLoadW (без атрибута экспорта).</summary>
        /// <param name="parentWin">Родительское окно Lister.</param>
        /// <param name="fileToLoad">Путь к файлу.</param>
        /// <returns>HWND окна плагина или NULL при отказе.</returns>
        private static IntPtr ListLoadCore(IntPtr parentWin, string fileToLoad)
        {
            try
            {
                if (parentWin == IntPtr.Zero || !IsMarkdown(fileToLoad))
                    return IntPtr.Zero;

                var fi = new FileInfo(fileToLoad);
                if (!fi.Exists)
                    return IntPtr.Zero;

                // Защита от больших файлов — предупреждение перед чтением
                long limitBytes = (long)(Settings?.LargeFileWarningMb ?? 5) * 1024L * 1024L;
                if (limitBytes > 0 && fi.Length > limitBytes)
                {
                    DialogResult answer = MessageBox.Show(
                        Loc.T("LargeFile.Text", fi.Name,
                            fi.Length / 1048576.0,
                            Settings.LargeFileWarningMb),
                        "MarkdownLister",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (answer != DialogResult.Yes)
                        return IntPtr.Zero;
                }

                var control = new ListerControl(Settings);
                control.CreateControl();
                NativeMethods.SetParent(control.Handle, parentWin);

                NativeMethods.GetClientRect(parentWin, out NativeMethods.RECT rc);
                NativeMethods.MoveWindow(control.Handle, 0, 0,
                    rc.Right - rc.Left, rc.Bottom - rc.Top, true);

                control.LoadFile(fileToLoad);

                lock (Sync)
                    Windows[control.Handle] = control;

                return control.Handle;
            }
            catch (Exception ex)
            {
                Logger.Error("ListLoad", ex);
                return IntPtr.Zero;
            }
        }

        /// <summary>Создаёт окно просмотра для файла (Unicode-версия).</summary>
        /// <inheritdoc cref="ListLoad"/>
        [DllExport("ListLoadW", CallingConvention.StdCall)]
        public static IntPtr ListLoadW(IntPtr parentWin,
            [MarshalAs(UnmanagedType.LPWStr)] string fileToLoad, int showFlags)
        {
            return ListLoadCore(parentWin, fileToLoad);
        }

        /// <summary>Показывает следующий/предыдущий файл в том же окне.</summary>
        /// <param name="listWin">HWND окна плагина.</param>
        /// <param name="fileToLoad">Путь к файлу.</param>
        /// <param name="showFlags">Флаги режима показа.</param>
        /// <returns>0 — успех; 2 — файл не поддерживается.</returns>
        [DllExport("ListLoadNext", CallingConvention.StdCall)]
        public static int ListLoadNext(IntPtr listWin,
            [MarshalAs(UnmanagedType.LPStr)] string fileToLoad, int showFlags)
        {
            return ListLoadNextCore(listWin, fileToLoad);
        }

        /// <summary>Показывает следующий/предыдущий файл в том же окне (Unicode-версия).</summary>
        /// <inheritdoc cref="ListLoadNext"/>
        [DllExport("ListLoadNextW", CallingConvention.StdCall)]
        public static int ListLoadNextW(IntPtr listWin,
            [MarshalAs(UnmanagedType.LPWStr)] string fileToLoad, int showFlags)
        {
            return ListLoadNextCore(listWin, fileToLoad);
        }

        private static int ListLoadNextCore(IntPtr listWin, string fileToLoad)
        {
            try
            {
                if (!IsMarkdown(fileToLoad) || !File.Exists(fileToLoad))
                    return 2;

                ListerControl control = FindWindow(listWin);
                if (control == null)
                    return 1;

                control.LoadFile(fileToLoad);
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error("ListLoadNext", ex);
                return 1;
            }
        }

        /// <summary>Закрывает окно просмотра и освобождает ресурсы.</summary>
        /// <param name="listWin">HWND окна плагина.</param>
        /// <returns>0 — успех.</returns>
        [DllExport("ListCloseWindow", CallingConvention.StdCall)]
        public static int ListCloseWindow(IntPtr listWin)
        {
            try
            {
                ListerControl control;
                lock (Sync)
                {
                    if (!Windows.TryGetValue(listWin, out control))
                        return 0;
                    Windows.Remove(listWin);
                }

                if (control != null)
                {
                    if (control.IsHandleCreated)
                        control.Dispose();
                }
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error("ListCloseWindow", ex);
                return 0;
            }
        }

        /// <summary>Возвращает строку детекции файлов (какие файлы показывает плагин).</summary>
        /// <param name="detectString">Буфер под строку детекции.</param>
        /// <param name="maxLen">Размер буфера в байтах.</param>
        [DllExport("ListGetDetectString", CallingConvention.StdCall)]
        public static void ListGetDetectString(IntPtr detectString, int maxLen)
        {
            try
            {
                const string detect = "EXT=\"MD\" | EXT=\"MARKDOWN\" | EXT=\"MKD\" | EXT=\"MKDN\" | EXT=\"MDOWN\" | EXT=\"MDTEXT\"";
                byte[] bytes = Encoding.ASCII.GetBytes(detect + "\0");
                int n = Math.Min(bytes.Length, Math.Max(0, maxLen));
                if (n > 0)
                {
                    if (n == maxLen)
                        bytes[n - 1] = 0; // гарантируем завершающий \0
                    Marshal.Copy(bytes, 0, detectString, n);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("ListGetDetectString", ex);
            }
        }

        /// <summary>Открывает поиск по документу или переходит к следующему совпадению.</summary>
        /// <param name="listWin">HWND окна плагина.</param>
        /// <param name="findNext">0 — новый поиск, 1 — следующее совпадение.</param>
        /// <returns>1 — поиск обработан плагином.</returns>
        [DllExport("ListSearchDialog", CallingConvention.StdCall)]
        public static int ListSearchDialog(IntPtr listWin, int findNext)
        {
            try
            {
                ListerControl control = FindWindow(listWin);
                if (control == null)
                    return 0;

                if (findNext == 0)
                    control.OpenSearch();
                else
                    control.FindNext(findNext > 0);
                return 1;
            }
            catch (Exception ex)
            {
                Logger.Error("ListSearchDialog", ex);
                return 0;
            }
        }

        /// <summary>Обрабатывает команды Lister (копирование, выделение, новые параметры).</summary>
        /// <param name="listWin">HWND окна плагина.</param>
        /// <param name="command">lc_copy, lc_newparams, lc_selectall.</param>
        /// <param name="parameter">Параметр команды.</param>
        /// <returns>0 — команда выполнена.</returns>
        [DllExport("ListSendCommand", CallingConvention.StdCall)]
        public static int ListSendCommand(IntPtr listWin, int command, int parameter)
        {
            try
            {
                ListerControl control = FindWindow(listWin);
                if (control == null)
                    return 1;

                switch (command)
                {
                    case NativeMethods.lc_copy:
                        control.CopySelection();
                        return 0;
                    case NativeMethods.lc_newparams:
                        control.ReloadSettings();
                        return 0;
                    case NativeMethods.lc_selectall:
                        control.SelectAll();
                        return 0;
                    default:
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("ListSendCommand", ex);
                return 1;
            }
        }

        /// <summary>Обрабатывает уведомления окна (WM_NOTIFY / WM_COMMAND) от Total Commander.</summary>
        /// <param name="listWin">HWND окна плагина.</param>
        /// <param name="message">Код уведомления.</param>
        /// <param name="wParam">wParam сообщения.</param>
        /// <param name="lParam">lParam сообщения.</param>
        /// <returns>1 — уведомление обработано.</returns>
        [DllExport("ListNotificationReceived", CallingConvention.StdCall)]
        public static int ListNotificationReceived(IntPtr listWin, int message,
            IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (message == NativeMethods.WM_SIZE)
                {
                    // TC изменил размер окна — подгоняем дочерние контролы
                    ListerControl control = FindWindow(listWin);
                    if (control != null)
                    {
                        NativeMethods.GetClientRect(listWin, out NativeMethods.RECT rc);
                        NativeMethods.MoveWindow(control.Handle, 0, 0,
                            rc.Right - rc.Left, rc.Bottom - rc.Top, true);
                        return 1;
                    }
                }
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error("ListNotificationReceived", ex);
                return 0;
            }
        }

        /// <summary>Печать документа через штатный диалог печати WebView2.</summary>
        /// <param name="listWin">HWND окна плагина.</param>
        /// <param name="fileToPrint">Путь к печатаемому файлу.</param>
        /// <param name="defPrinter">Принтер по умолчанию.</param>
        /// <param name="printFlags">Флаги печати.</param>
        /// <param name="margins">Поля страницы.</param>
        /// <returns>0 — печать запущена.</returns>
        [DllExport("ListPrint", CallingConvention.StdCall)]
        public static int ListPrint(IntPtr listWin,
            [MarshalAs(UnmanagedType.LPStr)] string fileToPrint,
            [MarshalAs(UnmanagedType.LPStr)] string defPrinter,
            int printFlags, IntPtr margins)
        {
            try
            {
                ListerControl control = FindWindow(listWin);
                if (control == null)
                    return 1;

                control.ShowPrint();
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error("ListPrint", ex);
                return 1;
            }
        }

        /// <summary>Получает от TC параметры по умолчанию (версия интерфейса, путь к INI TC).</summary>
        /// <param name="dps">Структура параметров.</param>
        [DllExport("ListSetDefaultParams", CallingConvention.StdCall)]
        public static void ListSetDefaultParams(IntPtr dps)
        {
            try
            {
                if (dps == IntPtr.Zero)
                    return;

                var par = (NativeMethods.ListDefaultParamStruct)
                    Marshal.PtrToStructure(dps, typeof(NativeMethods.ListDefaultParamStruct));
                Logger.Info($"ListSetDefaultParams: INI TC = {par.DefaultIniName}, " +
                            $"interface = {par.PluginInterfaceVersionHigh}.{par.PluginInterfaceVersionLow}");
            }
            catch (Exception ex)
            {
                Logger.Error("ListSetDefaultParams", ex);
            }
        }

        private static ListerControl FindWindow(IntPtr listWin)
        {
            lock (Sync)
            {
                ListerControl control;
                Windows.TryGetValue(listWin, out control);
                return control;
            }
        }
    }
}
