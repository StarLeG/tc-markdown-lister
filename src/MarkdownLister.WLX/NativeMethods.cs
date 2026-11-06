// Автор: Старинский Олег Григорьевич (info@electroman.by, @ElectroManBY)
// WLX-плагин MarkdownLister: Win32-интероп для окна Lister.

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MarkdownLister.WLX
{
    /// <summary>Константы и функции Win32, необходимые плагину.</summary>
    internal static class NativeMethods
    {
        // Сообщения окна
        public const int WM_SIZE = 0x0005;
        public const int WM_NOTIFY = 0x004E;
        public const int WM_COMMAND = 0x0111;
        public const int WM_CONTEXTMENU = 0x007B;
        public const int WM_CLOSE = 0x0010;

        // Стили окон
        public const int GWL_STYLE = -16;
        public const int WS_CHILD = 0x40000000;

        // Флаги ShowFlags для ListLoad/ListLoadNext
        public const int lcp_wraptext = 0x01;
        public const int lcp_fittowindow = 0x02;
        public const int lcp_ansi = 0x04;
        public const int lcp_ascii = 0x08;
        public const int lcp_variable = 0x10;
        public const int lcp_forceshow = 0x40;

        // Команды ListSendCommand
        public const int lc_copy = 1;
        public const int lc_newparams = 2;
        public const int lc_selectall = 3;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll")]
        public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        public static extern bool SetFocus(IntPtr hWnd);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        /// <summary>Прямоугольник Win32.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        /// <summary>
        /// Структура параметров по умолчанию из ListSetDefaultParams (WLX SDK, lister.h).
        /// Раскладка: int size; int PluginInterfaceVersionLow; int PluginInterfaceVersionHigh;
        /// char DefaultIniName[MAX_PATH] (ANSI, 260 символов) — путь к INI Total Commander.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct ListDefaultParamStruct
        {
            public int size;
            public int PluginInterfaceVersionLow;
            public int PluginInterfaceVersionHigh;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string DefaultIniName;
        }
    }
}
