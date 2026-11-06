// Регресс-тест бинарников WLX: LoadLibrary + вызов экспортных функций без Total Commander.
//
// Зачем: падения плагина (0xe0434352) ловятся здесь с точным типом исключения,
// а не отладкой по краху TOTALCMD64.EXE. Проверяет резолв зависимостей (Bootstrap),
// статический конструктор Plugin и простейшие экспорты.
//
// Сборка и запуск (PowerShell):
//   C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /out:WlxHost.exe WlxHost.cs
//   .\WlxHost.exe "C:\Program Files\totalcmd\Plugins\wlx\MarkdownLister\MarkdownLister.wlx64"
//
// Опционально вторым аргументом — путь к тестовому .md (вызов ListLoadW).
//
// Автор: Старинский Олег Григорьевич; email: info@electroman.by; телеграм: @ElectroManBY.
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

internal static class WlxHost
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string path);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr h, string name);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr ListGetDetectStringDelegate(StringBuilder dest, int maxlen);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void ListSetDefaultParamsDelegate(IntPtr dps);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate IntPtr ListLoadWDelegate(IntPtr parent, string file, int flags);

    private static void Log(string s)
    {
        Console.WriteLine(s);
        File.AppendAllText("wlxhost.log", s + Environment.NewLine);
    }

    private static int Main(string[] args)
    {
        AppDomain.CurrentDomain.FirstChanceException += (s, e) =>
            Log("[first-chance] " + e.Exception.GetType().FullName + ": " + e.Exception.Message);
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            Log("[UNHANDLED] " + (e.ExceptionObject as Exception));

        if (args.Length < 1) { Console.WriteLine("usage: WlxHost.exe <wlx> [file.md]"); return 2; }
        string wlx = Path.GetFullPath(args[0]);
        string md = args.Length > 1 ? Path.GetFullPath(args[1]) : null;

        Log("== LoadLibrary: " + wlx);
        IntPtr h = LoadLibrary(wlx);
        if (h == IntPtr.Zero)
        {
            Log("LoadLibrary FAILED, Win32=" + Marshal.GetLastWin32Error());
            return 1;
        }
        Log("LoadLibrary OK, handle=0x" + h.ToString("X"));

        try
        {
            IntPtr p = GetProcAddress(h, "ListGetDetectString");
            Log("ListGetDetectString ptr=0x" + p.ToString("X"));
            if (p != IntPtr.Zero)
            {
                var sb = new StringBuilder(512);
                var fn = (ListGetDetectStringDelegate)Marshal.GetDelegateForFunctionPointer(
                    p, typeof(ListGetDetectStringDelegate));
                Log("-- calling ListGetDetectString...");
                IntPtr r = fn(sb, sb.Capacity);
                Log("-- returned 0x" + r.ToString("X") + " detect=\"" + sb + "\"");
            }

            p = GetProcAddress(h, "ListSetDefaultParams");
            Log("ListSetDefaultParams ptr=0x" + p.ToString("X"));
            if (p != IntPtr.Zero)
            {
                // Структура WLX SDK (lister.h): int size; int Low; int High; char DefaultIniName[260] (ANSI).
                int size = 4 + 4 + 4 + 260;
                IntPtr mem = Marshal.AllocHGlobal(size);
                for (int i = 0; i < size; i++) Marshal.WriteByte(mem, i, 0);
                Marshal.WriteInt32(mem, 0, size);
                Marshal.WriteInt32(mem, 4, 3);   // PluginInterfaceVersionLow
                Marshal.WriteInt32(mem, 8, 2);   // PluginInterfaceVersionHigh
                byte[] ini = Encoding.ASCII.GetBytes("C:\\FakeTC\\wincmd.ini");
                Marshal.Copy(ini, 0, mem + 12, ini.Length);

                var fn = (ListSetDefaultParamsDelegate)Marshal.GetDelegateForFunctionPointer(
                    p, typeof(ListSetDefaultParamsDelegate));
                Log("-- calling ListSetDefaultParams...");
                fn(mem);
                Marshal.FreeHGlobal(mem);
                Log("-- returned");
            }

            if (md != null)
            {
                p = GetProcAddress(h, "ListLoadW");
                Log("ListLoadW ptr=0x" + p.ToString("X"));
                if (p != IntPtr.Zero)
                {
                    var fn = (ListLoadWDelegate)Marshal.GetDelegateForFunctionPointer(
                        p, typeof(ListLoadWDelegate));
                    Log("-- calling ListLoadW(" + md + ")...");
                    IntPtr w = fn(IntPtr.Zero, md, 0);
                    Log("-- returned hwnd=0x" + w.ToString("X"));
                }
            }
        }
        catch (Exception ex)
        {
            Log("[CAUGHT IN HOST] " + ex);
        }
        Log("== done");
        return 0;
    }
}
