// Обёртка над coreclr-ildasm для net.r_eg.DllExport.
//
// Причина: ildasm из форка 3F/coreclr (9.4.0) закрывает тела P/Invoke-методов
// голой «}» без комментария «// end of method ...». Парсер DllExport
// (MethodParserAction) завершает метод только по «} // end of method ...»
// или по голой «}» при Method.Pinvokeimpl != null (выставляется лишь для
// экспортируемых классов). Из-за этого P/Invoke-метод НЕэкспортируемого
// класса «залипает» в ParserState.Method — все следующие типы (в т.ч.
// экспортные) не парсятся, 0 unmanaged-экспортов.
//
// Обёртка запускает настоящий ildasm (ildasm.orig.exe), затем дописывает
// к пустым телам комментарий «// end of method patched» (парсеру важен
// только префикс «} // end of method») и ПЕРЕЗАПИСЫВАЕТ файл в UTF-8 с BOM.
// Перезапись обязательна: при выводе /unicode ildasm может оставить BOM
// UTF-16 при теле UTF-8 (рассинхрон), ilasm такой файл не разбирает.
//
// Автор: Старинский Олег Григорьевич; email: info@electroman.by; телеграм: @ElectroManBY.
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

internal static class Program
{
    /// <summary>Пустое тело метода в выводе ildasm: «{» и «}» на соседних строках.</summary>
    private static readonly Regex EmptyBodyRe = new Regex(
        @"(?<pre>^[ \t]*\{[ \t]*\r?\n[ \t]*)(?<close>\})(?<trail>[ \t]*\r?$)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>Запускает ildasm.orig.exe, патчит пустые тела и нормализует кодировку выхода.</summary>
    private static int Main(string[] args)
    {
        string selfDir = AppDomain.CurrentDomain.BaseDirectory;
        string realIlDasm = Path.Combine(selfDir, "ildasm.orig.exe");

        var psi = new ProcessStartInfo(realIlDasm)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string a in args)
        {
            if (psi.Arguments.Length > 0) psi.Arguments += " ";
            psi.Arguments += Quote(a);
        }

        int exitCode;
        using (var p = Process.Start(psi))
        {
            p.WaitForExit();
            exitCode = p.ExitCode;
        }
        if (exitCode != 0) return exitCode;

        string ilPath = FindOutputIl(args);
        if (ilPath != null && File.Exists(ilPath))
        {
            string text = ReadIl(File.ReadAllBytes(ilPath));
            string patched = EmptyBodyRe.Replace(text, "${pre}${close} // end of method patched${trail}");
            // Всегда переписываем в UTF-8 с BOM: ilasm coreclr определяет
            // кодировку по BOM («Source file is UTF-8» / «UNICODE»), а ildasm
            // /unicode может выдать BOM UTF-16 при теле UTF-8 — такой файл
            // ilasm не разбирает (syntax error).
            File.WriteAllText(ilPath, patched, new UTF8Encoding(true));
        }
        return exitCode;
    }

    /// <summary>Декодирует IL независимо от фактической кодировки (UTF-16 LE/BOM, UTF-8/BOM, без BOM).</summary>
    private static string ReadIl(byte[] raw)
    {
        int skip = 0;
        Encoding enc = new UTF8Encoding(false);
        if (raw.Length >= 2 && raw[0] == 0xFF && raw[1] == 0xFE)
        {
            // BOM UTF-16 LE, но тело могло быть записано как UTF-8:
            // в UTF-16 LE ASCII-текст даёт много нулевых байтов.
            int n = Math.Min(raw.Length - 2, 512);
            int zeros = 0;
            for (int i = 2; i < 2 + n; i++) if (raw[i] == 0) zeros++;
            if (zeros > n / 8) enc = Encoding.Unicode;
            skip = 2;
        }
        else if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
        {
            skip = 3;
        }
        return enc.GetString(raw, skip, raw.Length - skip);
    }

    /// <summary>Определяет путь к выходному .il из аргументов ildasm (/out=..., /out:..., либо имя входной DLL с .il).</summary>
    private static string FindOutputIl(string[] args)
    {
        string outPath = null;
        string inPath = null;
        foreach (string a in args)
        {
            if (a.StartsWith("/out:", StringComparison.OrdinalIgnoreCase)) outPath = a.Substring(5).Trim('"');
            else if (a.StartsWith("/out=", StringComparison.OrdinalIgnoreCase)) outPath = a.Substring(5).Trim('"');
            else if (a.Length > 0 && a[0] != '/') inPath = a.Trim('"');
        }
        if (outPath != null) return outPath;
        if (inPath != null) return Path.ChangeExtension(inPath, ".il");
        return null;
    }

    private static string Quote(string a)
    {
        return a.IndexOf(' ') >= 0 ? "\"" + a + "\"" : a;
    }
}
