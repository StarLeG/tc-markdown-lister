// Автор: Старинский Олег Григорьевич (info@electroman.by, @ElectroManBY)
// Ядро MarkdownLister: рендеринг Markdown в HTML через Markdig.

using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Extensions.Yaml;

namespace MarkdownLister.Core
{
    /// <summary>Рендерит Markdown в HTML с расширениями GFM.</summary>
    public sealed class MarkdownRenderer
    {
        private readonly MarkdownPipeline _pipeline;

        /// <summary>Создаёт рендерер с полным набором расширений (GFM, сноски, задачи, front matter).</summary>
        public MarkdownRenderer()
        {
            _pipeline = new MarkdownPipelineBuilder()
                // Якоря заголовков — в стиле GitHub (кириллица сохраняется), чтобы клики
                // по оглавлениям документов работали. Должно идти ДО UseAdvancedExtensions:
                // Advanced добавляет AutoIdentifiers с Default-алгоритмом (id вида "i",
                // "section"), а повторный вызов UseAutoIdentifiers существующее расширение
                // не заменяет.
                .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
                .UseAdvancedExtensions()   // таблицы, сноски, списки задач, автоссылки и др.
                .UseYamlFrontMatter()      // YAML front matter (игнорируется при рендеринге)
                .Build();
        }

        /// <summary>Преобразует текст Markdown в фрагмент HTML.</summary>
        /// <param name="markdown">Исходный текст Markdown.</param>
        /// <returns>HTML-фрагмент (без обёртки &lt;html&gt;).</returns>
        public string ToHtml(string markdown)
        {
            if (string.IsNullOrEmpty(markdown))
                return string.Empty;
            return Markdown.ToHtml(markdown, _pipeline);
        }

        /// <summary>
        /// Преобразует текст Markdown в HTML с деградацией при отказе парсера:
        /// патологический контент (например, тысячи конструкций «[текст][метка]»
        /// в одном абзаце) превышает лимит глубины вложенности Markdig — такой
        /// фрагмент показывается экранированным текстом, чтобы просмотр не падал.
        /// </summary>
        /// <param name="markdown">Исходный текст Markdown.</param>
        /// <param name="degraded">true — парсер отказался рендерить, отдан текст.</param>
        /// <returns>HTML-фрагмент (без обёртки &lt;html&gt;).</returns>
        public string ToHtmlSafe(string markdown, out bool degraded)
        {
            degraded = false;
            if (string.IsNullOrEmpty(markdown))
                return string.Empty;

            try
            {
                return Markdown.ToHtml(markdown, _pipeline);
            }
            catch
            {
                degraded = true;
                return ToPlainHtml(markdown);
            }
        }

        /// <summary>Экранирует текст и оборачивает его в блок с сохранением переносов строк.</summary>
        private static string ToPlainHtml(string text)
        {
            return "<p class=\"md-plain\">" +
                System.Net.WebUtility.HtmlEncode(text) + "</p>";
        }

        /// <summary>Извлекает заголовки документа для построения оглавления.</summary>
        /// <param name="markdown">Исходный текст Markdown.</param>
        /// <returns>Список заголовков с уровнями и якорями.</returns>
        public System.Collections.Generic.List<Heading> ExtractHeadings(string markdown)
        {
            var result = new System.Collections.Generic.List<Heading>();
            if (string.IsNullOrEmpty(markdown))
                return result;

            var idCounts = new System.Collections.Generic.Dictionary<string, int>();

            // Построчный перебор по индексам — без копий текста (Replace + Split
            // на файле в 100 МБ создавали бы строки по 200 МБ в LOH).
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

                // Пропускаем содержимое блоков кода (``` или ~~~)
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
                    if (level > 6 || (i < contentEnd && markdown[i] != ' '))
                        continue;

                    var text = markdown.Substring(i, contentEnd - i).Trim();
                    // Убираем закрывающие решётки в стиле ATX: "## Заголовок ##"
                    text = text.TrimEnd('#').TrimEnd();
                    if (text.Length == 0)
                        continue;

                    var id = MakeAnchor(text, idCounts);
                    result.Add(new Heading(level, text, id));
                }
            }
            return result;
        }

        /// <summary>Генерирует якорь в стиле GitHub (транслит не используется: кириллица сохраняется).</summary>
        private static string MakeAnchor(string text, System.Collections.Generic.Dictionary<string, int> idCounts)
        {
            var sb = new System.Text.StringBuilder(text.Length);
            bool lastDash = false;
            foreach (var ch in text.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch))
                {
                    sb.Append(ch);
                    lastDash = false;
                }
                else if (!lastDash && sb.Length > 0)
                {
                    sb.Append('-');
                    lastDash = true;
                }
            }
            var id = sb.ToString().Trim('-');
            if (id.Length == 0)
                id = "section";

            // Уникализация повторяющихся якорей: section, section-1, section-2...
            if (idCounts.TryGetValue(id, out int n))
            {
                idCounts[id] = n + 1;
                id = $"{id}-{n}";
            }
            else
            {
                idCounts[id] = 1;
            }
            return id;
        }
    }

    /// <summary>Заголовок документа для оглавления.</summary>
    public sealed class Heading
    {
        /// <summary>Уровень заголовка (1–6).</summary>
        public int Level { get; }
        /// <summary>Текст заголовка без символов #.</summary>
        public string Text { get; }
        /// <summary>Якорь для перехода.</summary>
        public string Id { get; }

        /// <summary>Создаёт заголовок.</summary>
        public Heading(int level, string text, string id)
        {
            Level = level;
            Text = text;
            Id = id;
        }
    }
}
