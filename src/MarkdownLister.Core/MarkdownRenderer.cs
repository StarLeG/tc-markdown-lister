// Автор: Старинский Олег Григорьевич (info@electroman.by, @ElectroManBY)
// Ядро MarkdownLister: рендеринг Markdown в HTML через Markdig.

using Markdig;
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

        /// <summary>Извлекает заголовки документа для построения оглавления.</summary>
        /// <param name="markdown">Исходный текст Markdown.</param>
        /// <returns>Список заголовков с уровнями и якорями.</returns>
        public System.Collections.Generic.List<Heading> ExtractHeadings(string markdown)
        {
            var result = new System.Collections.Generic.List<Heading>();
            if (string.IsNullOrEmpty(markdown))
                return result;

            var idCounts = new System.Collections.Generic.Dictionary<string, int>();
            var lines = markdown.Replace("\r\n", "\n").Split('\n');
            bool inFence = false;
            string fenceMarker = null;

            foreach (var line in lines)
            {
                // Пропускаем содержимое блоков кода (``` или ~~~)
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

                if (line.Length > 1 && line[0] == '#' && (line.Length == 1 || line[1] == ' '))
                {
                    int level = 0;
                    while (level < line.Length && line[level] == '#')
                        level++;
                    if (level > 6 || (level < line.Length && line[level] != ' '))
                        continue;

                    var text = line.Substring(level).Trim();
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
