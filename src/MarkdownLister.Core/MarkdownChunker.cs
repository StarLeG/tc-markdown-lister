// Автор: Старинский Олег Григорьевич (info@electroman.by, @ElectroManBY)
// Ядро MarkdownLister: резка Markdown-текста на чанки для постепенного рендеринга.

using System.Collections.Generic;

namespace MarkdownLister.Core
{
    /// <summary>
    /// Режет Markdown-текст на чанки ограниченного размера — для постепенного
    /// рендеринга больших документов без блокировки UI.
    /// Границы чанков — границы строк; предпочтительная точка разреза — пустая
    /// строка вне блока кода, не разрывающая списки и их продолжения. Если
    /// «безопасной» точки нет, делается жёсткий разрез на границе строки; разрез
    /// внутри блока кода дополняется маркерами fence, чтобы содержимое осталось
    /// кодом в обоих чанках. Экстремальный случай — одна строка длиннее лимита —
    /// разрезается посередине (с сохранением суррогатных пар UTF-16).
    /// </summary>
    public static class MarkdownChunker
    {
        /// <summary>Целевой размер чанка в символах (по умолчанию ~1 МБ текста).</summary>
        public const int DefaultChunkChars = 1_000_000;

        /// <summary>
        /// Последовательно отдаёт чанки текста суммарного размера ~<paramref name="chunkChars"/>.
        /// </summary>
        /// <param name="text">Исходный текст Markdown.</param>
        /// <param name="chunkChars">Целевой размер чанка в символах (не менее 1024).</param>
        /// <param name="wrapFences">
        /// Допечтывать маркеры fence при разрезе внутри блока кода (true — для рендеринга
        /// Markdown, false — для «сырого» исходника, где текст должен остаться нетронутым).
        /// </param>
        public static IEnumerable<string> Split(string text, int chunkChars = DefaultChunkChars, bool wrapFences = true)
        {
            if (string.IsNullOrEmpty(text))
                yield break;
            if (chunkChars < 1024)
                chunkChars = 1024;

            int chunkStart = 0;        // начало текущего чанка
            int splitCandidate = -1;   // точка после пустой строки — безопасное место разреза
            string carry = null;       // допечатанный открывающий маркер fence для следующего чанка

            bool inFence = false;
            char fenceChar = '\0';
            int fenceRunLen = 3;
            bool prevSensitive = true; // до первой строки «продолжений» не бывает

            int pos = 0;
            while (pos < text.Length)
            {
                int lineStart = pos;
                int nl = text.IndexOf('\n', pos);
                int lineEnd = nl >= 0 ? nl : text.Length;
                int contentEnd = lineEnd > lineStart && text[lineEnd - 1] == '\r' ? lineEnd - 1 : lineEnd;
                int nextPos = nl >= 0 ? nl + 1 : text.Length;

                int first = lineStart;
                while (first < contentEnd && (text[first] == ' ' || text[first] == '\t'))
                    first++;

                bool blank = first >= contentEnd;
                bool sensitive = false;

                if (!blank)
                {
                    char c = text[first];
                    if (IsFenceLine(text, first, contentEnd, out int runLen))
                    {
                        if (!inFence)
                        {
                            inFence = true;
                            fenceChar = c;
                            fenceRunLen = runLen;
                        }
                        else if (c == fenceChar)
                        {
                            inFence = false;
                            fenceChar = '\0';
                        }
                    }
                    else if (IsSensitiveStart(text, first, contentEnd))
                    {
                        sensitive = true;
                    }
                }

                // Кандидат на разрез — точка после пустой строки вне fence,
                // если вокруг неё нет «опасных» соседей (списки, цитаты, таблицы,
                // отступленные продолжения).
                if (blank && !inFence && !prevSensitive && nextPos < text.Length)
                {
                    int nls = text.IndexOf('\n', nextPos);
                    int nextLineEnd = nls >= 0 ? nls : text.Length;
                    int nextContentEnd = nextLineEnd > nextPos && text[nextLineEnd - 1] == '\r'
                        ? nextLineEnd - 1 : nextLineEnd;
                    int nf = nextPos;
                    while (nf < nextContentEnd && (text[nf] == ' ' || text[nf] == '\t'))
                        nf++;

                    bool nextIndented = nextPos < nextContentEnd &&
                        (text[nextPos] == ' ' || text[nextPos] == '\t');
                    if (nf < nextContentEnd && !nextIndented && !IsSensitiveStart(text, nf, nextContentEnd))
                        splitCandidate = nextPos;
                }

                prevSensitive = sensitive;

                // Решение о разрезе — после обработки строки. Разрез только по
                // смещениям: уже просканированный «хвост» [cut, nextPos) попадёт в
                // следующий чанк при Substring, повторный проход строк не нужен
                // (иначе состояние fence переключится дважды).
                if (nextPos - chunkStart >= chunkChars)
                {
                    int cut = splitCandidate > chunkStart ? splitCandidate : nextPos;

                    // Одна строка не помещается даже в двойной лимит — жёсткий разрез
                    if (cut == nextPos && splitCandidate <= chunkStart &&
                        nextPos - chunkStart >= chunkChars * 2)
                    {
                        cut = chunkStart + chunkChars;
                        if (cut > chunkStart && char.IsHighSurrogate(text[cut - 1]))
                            cut--; // не разрывать суррогатную пару UTF-16
                    }

                    // Разрез «внутри» fence (не по кандидату) — закрываем/открываем блок кода
                    bool wrap = wrapFences && inFence && cut < text.Length && cut != splitCandidate;

                    string chunk = (carry ?? string.Empty) + text.Substring(chunkStart, cut - chunkStart);
                    carry = null;
                    if (wrap)
                    {
                        chunk += "\n" + new string(fenceChar, fenceRunLen) + "\n";
                        carry = new string(fenceChar, fenceRunLen) + "\n";
                    }
                    yield return chunk;

                    chunkStart = cut;
                    splitCandidate = -1;
                }

                pos = nextPos;
            }

            if (chunkStart < text.Length)
                yield return (carry ?? string.Empty) + text.Substring(chunkStart);
        }

        /// <summary>Строка начинается с маркера блока кода (``` или ~~~ с продолжением).</summary>
        private static bool IsFenceLine(string text, int first, int contentEnd, out int runLen)
        {
            runLen = 0;
            if (first + 3 > contentEnd)
                return false;
            char c = text[first];
            if (c != '`' && c != '~')
                return false;
            if (text[first + 1] != c || text[first + 2] != c)
                return false;

            runLen = 3;
            while (first + runLen < contentEnd && text[first + runLen] == c)
                runLen++;
            return true;
        }

        /// <summary>
        /// Строка начинает блок, который нельзя разрывать пустой строкой:
        /// список, цитата, таблица, нумерованный пункт.
        /// </summary>
        private static bool IsSensitiveStart(string text, int first, int contentEnd)
        {
            if (first >= contentEnd)
                return false;

            char c = text[first];
            if (c == '-' || c == '*' || c == '+' || c == '>' || c == '|')
                return true;

            if (c >= '0' && c <= '9')
            {
                int j = first + 1;
                while (j < contentEnd && text[j] >= '0' && text[j] <= '9')
                    j++;
                if (j < contentEnd && (text[j] == '.' || text[j] == ')'))
                    return true;
            }
            return false;
        }
    }
}
