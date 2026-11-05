// Автор: Старинский Олег Григорьевич (info@electroman.by, @ElectroManBY)
// Ядро MarkdownLister: статистика документа для статус-строки.

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

        /// <summary>Вычисляет статистику по тексту Markdown.</summary>
        /// <param name="markdown">Исходный текст Markdown.</param>
        public static DocumentStats Compute(string markdown)
        {
            var stats = new DocumentStats();
            if (string.IsNullOrEmpty(markdown))
                return stats;

            // Подсчёт слов одним проходом: переход «пробел → не пробел» —
            // эквивалент \S+, но без миллиона Match-объектов на большом файле.
            bool inWord = false;
            foreach (char ch in markdown)
            {
                if (char.IsWhiteSpace(ch))
                {
                    inWord = false;
                }
                else if (!inWord)
                {
                    inWord = true;
                    stats.WordCount++;
                }
            }

            // Подсчёт заголовков — строки, начинающиеся с 1–6 решёток и пробела,
            // с учётом блоков кода (они не должны попадать в подсчёт).
            // Построчный перебор по индексам — без копий текста (Replace + Split).
            bool inFence = false;
            char fenceChar = '\0';
            int pos = 0;
            while (pos < markdown.Length)
            {
                int lineStart = pos;
                int nl = markdown.IndexOf('\n', pos);
                int lineEnd = nl >= 0 ? nl : markdown.Length;
                int contentEnd = lineEnd > lineStart && markdown[lineEnd - 1] == '\r'
                    ? lineEnd - 1 : lineEnd;
                pos = nl >= 0 ? nl + 1 : markdown.Length;

                int first = lineStart;
                while (first < contentEnd && (markdown[first] == ' ' || markdown[first] == '\t'))
                    first++;

                if (first + 3 <= contentEnd)
                {
                    char c = markdown[first];
                    if ((c == '`' || c == '~') &&
                        markdown[first + 1] == c && markdown[first + 2] == c)
                    {
                        if (!inFence)
                        {
                            inFence = true;
                            fenceChar = c;
                        }
                        else if (c == fenceChar)
                        {
                            inFence = false;
                            fenceChar = '\0';
                        }
                        continue;
                    }
                }
                if (inFence)
                    continue;

                if (lineStart < contentEnd && markdown[lineStart] == '#')
                {
                    int level = 0;
                    int i = lineStart;
                    while (i < contentEnd && markdown[i] == '#')
                    {
                        level++;
                        i++;
                    }
                    if (level <= 6 && (i == contentEnd || markdown[i] == ' '))
                        stats.HeadingCount++;
                }
            }

            stats.ReadingTimeMinutes = System.Math.Max(1, (stats.WordCount + WordsPerMinute - 1) / WordsPerMinute);
            return stats;
        }
    }
}
