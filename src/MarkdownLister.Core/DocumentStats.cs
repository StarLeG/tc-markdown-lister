// Автор: Старинский Олег Григорьевич (info@electroman.by, @ElectroManBY)
// Ядро MarkdownLister: статистика документа для статус-строки.

using System.Text.RegularExpressions;

namespace MarkdownLister.Core
{
    /// <summary>Статистика Markdown-документа: слова, заголовки, время чтения.</summary>
    public sealed class DocumentStats
    {
        /// <summary>Количество слов.</summary>
        public int WordCount { get; private set; }
        /// <summary>Количество заголовков.</summary>
        public int HeadingCount { get; private set; }
        /// <summary>Примерное время чтения в минутах (не менее 1).</summary>
        public int ReadingTimeMinutes { get; private set; }

        /// <summary>Средняя скорость чтения, слов в минуту.</summary>
        private const int WordsPerMinute = 200;

        private static readonly Regex WordRegex = new Regex(@"\S+", RegexOptions.Compiled);

        /// <summary>Вычисляет статистику по тексту Markdown.</summary>
        /// <param name="markdown">Исходный текст Markdown.</param>
        public static DocumentStats Compute(string markdown)
        {
            var stats = new DocumentStats();
            if (string.IsNullOrEmpty(markdown))
                return stats;

            stats.WordCount = WordRegex.Matches(markdown).Count;

            // Подсчёт заголовков — строки, начинающиеся с 1–6 решёток и пробела,
            // с учётом блоков кода (они не должны попадать в подсчёт).
            bool inFence = false;
            string fenceMarker = null;
            foreach (var line in markdown.Replace("\r\n", "\n").Split('\n'))
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
                {
                    if (!inFence)
                    {
                        inFence = true;
                        fenceMarker = trimmed.Substring(0, 3);
                    }
                    else if (trimmed.StartsWith(fenceMarker))
                    {
                        inFence = false;
                        fenceMarker = null;
                    }
                    continue;
                }
                if (inFence)
                    continue;

                if (line.Length > 0 && line[0] == '#')
                {
                    int level = 0;
                    while (level < line.Length && line[level] == '#')
                        level++;
                    if (level <= 6 && (level == line.Length || line[level] == ' '))
                        stats.HeadingCount++;
                }
            }

            stats.ReadingTimeMinutes = System.Math.Max(1, (stats.WordCount + WordsPerMinute - 1) / WordsPerMinute);
            return stats;
        }
    }
}
