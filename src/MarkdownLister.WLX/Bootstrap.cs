// Автор: Старинский Олег Григорьевич (info@electroman.by, @ElectroManBY)
// WLX-плагин MarkdownLister: ранняя инициализация (module initializer).
//
// Зачем: CLR резолвит зависимости (MarkdownLister.Core и др.) при JIT-компиляции
// метода — ДО выполнения его тела. Если подключать AssemblyResolve в .cctor
// Plugin, получается замкнутый круг: JIT .cctor требует Core, обработчик ещё не
// подключён, а стандартный поиск идёт от AppBase процесса (папка Total Commander),
// а не от папки плагина — FileNotFoundException и падение TC (0xe0434352).
// Module initializer выполняется при загрузке модуля, раньше JIT любого метода.

using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

// Для net48 атрибут определяем сами: в BCL он появился с .NET 5.
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class ModuleInitializerAttribute : Attribute
    {
    }
}

namespace MarkdownLister.WLX
{
    /// <summary>Ранняя инициализация плагина: папка модуля и резолв зависимостей.</summary>
    internal static class Bootstrap
    {
        private static bool _initialized;
        private static string _pluginDir;

        /// <summary>Папка плагина (каталог загруженного .wlx/.wlx64).</summary>
        internal static string PluginDir
        {
            get
            {
                EnsureInitialized();
                return _pluginDir;
            }
        }

        /// <summary>Подключает AssemblyResolve; вызывается CLR при загрузке модуля.</summary>
        [ModuleInitializer]
        internal static void EnsureInitialized()
        {
            if (_initialized)
                return;
            _initialized = true;
            _pluginDir = GetModuleDir();
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
        }

        private static string GetModuleDir()
        {
            try
            {
                // Assembly.Location при нативной загрузке (LoadLibrary из TC) бывает пустым —
                // спрашиваем путь модуля напрямую у Win32.
                var sb = new StringBuilder(1024);
                IntPtr h = Marshal.GetHINSTANCE(typeof(Bootstrap).Module);
                if (h != IntPtr.Zero && GetModuleFileName(h, sb, (uint)sb.Capacity) > 0)
                    return Path.GetDirectoryName(sb.ToString());
            }
            catch
            {
            }
            try
            {
                return Path.GetDirectoryName(typeof(Bootstrap).Assembly.Location)
                       ?? AppDomain.CurrentDomain.BaseDirectory;
            }
            catch
            {
                return AppDomain.CurrentDomain.BaseDirectory;
            }
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            try
            {
                string name = new AssemblyName(args.Name).Name + ".dll";
                string path = Path.Combine(_pluginDir ?? ".", name);
                if (!File.Exists(path))
                    return null;
                // UnsafeLoadFrom, а не LoadFrom: файлы из скачанного архива релиза
                // несут Zone.Identifier (Mark of the Web), и обычный LoadFrom
                // отказывается их загружать (FileLoadException, 0x80131515).
                // Тогда зависимость не резолвится, Plugin..cctor падает и
                // исключение уходит в Total Commander (крах 0xe0434352).
                // UnsafeLoadFrom обходит проверку зоны, сохраняя контекст
                // LoadFrom (Location) — нужен для поиска нативного WebView2Loader.dll.
                return Assembly.UnsafeLoadFrom(path);
            }
            catch
            {
                return null;
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern uint GetModuleFileName(IntPtr hModule, StringBuilder lpFilename, uint nSize);
    }
}
