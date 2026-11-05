// Автор: Старинский Олег Григорьевич (info@electroman.by, @ElectroManBY)
// WLX-плагин MarkdownLister: окно просмотра на базе WebView2 и мост к js/app.js.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using MarkdownLister.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MarkdownLister.WLX
{
    /// <summary>
    /// Контрол Lister: рендерит Markdown в WebView2, связывает страницу
    /// (js/app.js) с настройками, поиском, печатью и экспортом PDF.
    /// </summary>
    internal sealed class ListerControl : UserControl
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer
        {
            MaxJsonLength = int.MaxValue,
        };

        private const string VirtualHost = "app.markdownlister";

        /// <summary>Виртуальный хост для локальных картинок документа (папка документа).</summary>
        private const string MediaHost = "media.markdownlister";

        private readonly PluginSettings _settings;
        private readonly MarkdownRenderer _renderer = new MarkdownRenderer();

        private WebView2 _webView;
        private Label _fallback;
        private ContextMenuStrip _menu;

        private string _currentFile;
        private bool _ready;

        /// <summary>Корень (диск/шара), уже разданный через MediaHost (для отслеживания смены маппинга).</summary>
        private string _mappedRoot;

        /// <summary>Папка документа внутри корня, URL-стиль (например, <c>docs/images</c>), без хоста.</summary>
        private string _mediaPrefix;

        /// <summary>Страница template.html уже загружена (навигация выполнена).</summary>
        private bool _pageNavigated;

        /// <summary>Документ, ожидающий отправки в страницу (после готовности WebView2).</summary>
        private PendingDocument _pendingDoc;

        /// <summary>Отмена текущей загрузки (новый файл / закрытие окна).</summary>
        private CancellationTokenSource _loadCts;

        /// <summary>Поколение потоковой отправки: растёт при новой загрузке, гасит старые стримы.</summary>
        private int _streamId;

        /// <summary>Подготовленный к показу документ.</summary>
        private sealed class PendingDocument
        {
            /// <summary>Декодированный текст Markdown.</summary>
            public string Text;
            /// <summary>Отрендеренный HTML (только для небольших документов).</summary>
            public string Html;
            /// <summary>Заголовки для оглавления.</summary>
            public List<Heading> Headings;
            /// <summary>Статистика документа.</summary>
            public DocumentStats Stats;
            /// <summary>Размер файла в байтах (для предупреждения о большом файле).</summary>
            public long FileSizeBytes;
            /// <summary>Большой документ: рендерить и отправлять чанками.</summary>
            public bool Large;
        }

        private ToolStripMenuItem _themeSystem, _themeLight, _themeDark;
        private ToolStripMenuItem _langAuto, _langRu, _langEn;
        private ToolStripMenuItem _tocItem, _splitItem, _lineNumbersItem;

        /// <summary>Создаёт контрол с заданными настройками.</summary>
        /// <param name="settings">Настройки плагина.</param>
        public ListerControl(PluginSettings settings)
        {
            _settings = settings ?? new PluginSettings(Path.Combine(Bootstrap.PluginDir, "MarkdownLister.WLX.dll"));

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = SystemColors.Window;

            Loc.SetLanguage(_settings.Language);
            BuildContextMenu();
            CreateWebView();
        }

        // ---------------------------------------------------------------
        // Инициализация WebView2
        // ---------------------------------------------------------------

        private void CreateWebView()
        {
            try
            {
                _webView = new WebView2 { Dock = DockStyle.Fill };
                Controls.Add(_webView);
                _ = InitWebViewAsync();
            }
            catch (Exception ex)
            {
                Logger.Error("CreateWebView", ex);
                ShowFallback(ex.Message);
            }
        }

        private async Task InitWebViewAsync()
        {
            try
            {
                string userData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MarkdownLister", "WebView2");
                Directory.CreateDirectory(userData);

                var env = await CoreWebView2Environment.CreateAsync(null, userData,
                    new CoreWebView2EnvironmentOptions());

                await _webView.EnsureCoreWebView2Async(env);

                CoreWebView2 core = _webView.CoreWebView2;

                // Веб-ресурсы раздаются из папки web\ рядом с плагином
                string webRoot = Path.Combine(Plugin.PluginDir ?? ".", "web");
                core.SetVirtualHostNameToFolderMapping(VirtualHost, webRoot,
                    CoreWebView2HostResourceAccessKind.Allow);

                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.AreDevToolsEnabled = false;

                core.ContextMenuRequested += OnContextMenuRequested;
                core.WebMessageReceived += OnWebMessageReceived;

                // Маппинг папки документа должен существовать ДО загрузки страницы:
                // смена маппинга не применяется к уже загруженной странице (WebView2)
                MapDocumentMedia(_currentFile);

                core.Navigate($"https://{VirtualHost}/template.html");
                _pageNavigated = true;
            }
            catch (Exception ex)
            {
                Logger.Error("InitWebView", ex);
                ShowFallback(Loc.T("Err.WebView2", ex.Message));
            }
        }

        private void ShowFallback(string text)
        {
            if (IsDisposed)
                return;

            if (_webView != null)
            {
                Controls.Remove(_webView);
                _webView.Dispose();
                _webView = null;
            }

            if (_fallback == null)
            {
                _fallback = new Label
                {
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Padding = new Padding(24),
                    Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 10f),
                };
                Controls.Add(_fallback);
            }
            _fallback.Text = text;
        }

        // ---------------------------------------------------------------
        // Загрузка документа
        // ---------------------------------------------------------------

        /// <summary>Жёсткий предел размера файла, МБ (64-битный процесс).</summary>
        private const long MaxFileMb64 = 1024;

        /// <summary>Жёсткий предел размера файла, МБ (32-битный процесс: строка UTF-16 занимает ×2).</summary>
        private const long MaxFileMb32 = 200;

        /// <summary>
        /// Читает Markdown-файл и отправляет его в страницу. Чтение, парсинг и
        /// рендеринг выполняются вне UI-потока; большие файлы (от
        /// <see cref="PluginSettings.LargeFileWarningMb"/>) отправляются чанками
        /// с прогрессом — Lister не блокируется.
        /// </summary>
        /// <param name="path">Путь к файлу.</param>
        public async void LoadFile(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    SendError(Loc.T("Err.FileNotFound") + path);
                    return;
                }

                var fi = new FileInfo(path);
                long maxMb = Environment.Is64BitProcess ? MaxFileMb64 : MaxFileMb32;
                if (fi.Length > maxMb * 1024L * 1024L)
                {
                    SendError(Loc.T("Err.FileTooBig", fi.Length / 1048576.0, maxMb));
                    return;
                }

                // Новая загрузка отменяет предыдущую (в т.ч. идущий стрим чанков)
                _loadCts?.Cancel();
                _loadCts = new CancellationTokenSource();
                CancellationToken token = _loadCts.Token;
                _streamId++; // старые стримы гаснут по несовпадению поколения

                bool large = fi.Length >= (long)(_settings.LargeFileWarningMb) * 1024L * 1024L;

                _currentFile = path;
                MapDocumentMedia(path); // до загрузки страницы — без перезагрузки
                Send(new Dictionary<string, object> { ["type"] = "loading" });

                // Фоновая часть: чтение, декодирование, статистика, рендеринг
                PendingDocument doc = await Task.Run(() =>
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    var d = new PendingDocument
                    {
                        Text = EncodingDetector.Decode(bytes, out _),
                        FileSizeBytes = fi.Length,
                        Large = large,
                    };
                    d.Headings = _renderer.ExtractHeadings(d.Text);
                    d.Stats = DocumentStats.Compute(d.Text);
                    if (!d.Large)
                    {
                        d.Html = _renderer.ToHtmlSafe(d.Text, out bool degraded);
                        if (degraded)
                            Logger.Info("LoadFile: парсер отказался рендерить документ — показан текстом");
                    }
                    return d;
                }, token);

                if (token.IsCancellationRequested || IsDisposed)
                    return;

                _pendingDoc = doc;
                PushDocument();
            }
            catch (OperationCanceledException)
            {
                // Загрузка отменена (новый файл / закрытие окна) — молча выходим.
            }
            catch (Exception ex)
            {
                Logger.Error("LoadFile", ex);
                SendError(Loc.T("Err.LoadFile") + ex.Message);
            }
        }

        private void PushDocument()
        {
            if (!_ready || _pendingDoc == null)
                return;

            // Папка документа → виртуальный хост: относительные картинки
            // (docs/images/...) должны загружаться оттуда, а не из web\ плагина.
            // Смена маппинга не действует на уже загруженную страницу (WebView2) —
            // в этом случае перезагружаем её; документ придёт после 'ready'.
            if (MapDocumentMedia(_currentFile) && _pageNavigated)
            {
                _webView.CoreWebView2.Reload();
                return;
            }

            if (_pendingDoc.Large)
            {
                StreamLargeDocument();
                return;
            }

            var msg = new Dictionary<string, object>
            {
                ["type"] = "load",
                ["html"] = _pendingDoc.Html,
                ["source"] = _pendingDoc.Text,
                ["headings"] = HeadingsToDto(_pendingDoc.Headings),
                ["settings"] = SettingsToDto(),
                ["stats"] = StatsToDto(_pendingDoc.Stats),
                ["imageBase"] = ImageBaseUrl,
                ["imageRootBase"] = "https://" + MediaHost + "/",
                ["imageRoot"] = _mappedRoot ?? "",
            };
            Send(msg);
        }

        /// <summary>
        /// Постепенно рендерит и отправляет большой документ чанками:
        /// load-begin → load-chunk (с процентами) → load-end. UI-поток свободен
        /// между чанками, страница показывает текст по мере готовности.
        /// </summary>
        private async void StreamLargeDocument()
        {
            int id = ++_streamId;
            PendingDocument doc = _pendingDoc;
            CancellationToken token = _loadCts?.Token ?? CancellationToken.None;

            try
            {
                Send(new Dictionary<string, object>
                {
                    ["type"] = "load-begin",
                    ["headings"] = HeadingsToDto(doc.Headings),
                    ["settings"] = SettingsToDto(),
                    ["stats"] = StatsToDto(doc.Stats),
                    ["imageBase"] = ImageBaseUrl,
                    ["imageRootBase"] = "https://" + MediaHost + "/",
                    ["imageRoot"] = _mappedRoot ?? "",
                    ["size"] = doc.FileSizeBytes,
                });

                long total = doc.Text.Length;
                long processed = 0;
                bool degradedLogged = false;
                foreach (string chunk in MarkdownChunker.Split(doc.Text))
                {
                    token.ThrowIfCancellationRequested();
                    if (id != _streamId || IsDisposed)
                        return;

                    bool chunkDegraded = false;
                    string html = await Task.Run(() =>
                    {
                        string h = _renderer.ToHtmlSafe(chunk, out bool degraded);
                        chunkDegraded = degraded;
                        return h;
                    }, token);
                    token.ThrowIfCancellationRequested();
                    if (id != _streamId || IsDisposed)
                        return;

                    if (chunkDegraded && !degradedLogged)
                    {
                        degradedLogged = true;
                        Logger.Info("StreamLargeDocument: часть чанков показана текстом (отказ парсера)");
                    }

                    processed += chunk.Length;
                    Send(new Dictionary<string, object>
                    {
                        ["type"] = "load-chunk",
                        ["html"] = html,
                        ["percent"] = total > 0 ? (int)(processed * 100L / total) : 100,
                    });
                }

                if (id != _streamId)
                    return;
                Send(new Dictionary<string, object> { ["type"] = "load-end" });
            }
            catch (OperationCanceledException)
            {
                // Загрузка отменена (новый файл / закрытие окна) — молча выходим.
            }
            catch (Exception ex)
            {
                Logger.Error("StreamLargeDocument", ex);
                if (id == _streamId)
                    SendError(Loc.T("Err.LoadFile") + ex.Message);
            }
        }

        /// <summary>
        /// Стримит исходный Markdown в страницу по запросу (need-source) —
        /// только для больших документов: split view включается не всегда,
        /// и держать второй копией 100 МБ в DOM незачем.
        /// </summary>
        private async void SendSourceStream()
        {
            int id = _streamId;
            PendingDocument doc = _pendingDoc;
            if (doc == null || !doc.Large)
                return;

            try
            {
                foreach (string chunk in MarkdownChunker.Split(doc.Text, wrapFences: false))
                {
                    if (id != _streamId || IsDisposed)
                        return;
                    Send(new Dictionary<string, object> { ["type"] = "source-chunk", ["text"] = chunk });
                    await Task.Yield(); // отдать UI-поток между сериализациями чанков
                }

                if (id != _streamId)
                    return;
                Send(new Dictionary<string, object> { ["type"] = "source-end" });
            }
            catch (Exception ex)
            {
                Logger.Error("SendSourceStream", ex);
            }
        }

        /// <summary>Раздаёт корень документа (диск/шару) через виртуальный хост для локальных
        /// картинок: так работают относительные пути с <c>../</c> и абсолютные пути того же
        /// корня. Страница санитизирована, CSP ограничивает ресурсы картинками (img-src),
        /// скрипты берутся только из web\ плагина.</summary>
        /// <param name="docPath">Путь к просматриваемому файлу.</param>
        /// <returns>true — корень раздачи был установлен/изменён сейчас.</returns>
        private bool MapDocumentMedia(string docPath)
        {
            try
            {
                if (string.IsNullOrEmpty(docPath))
                    return false;

                string full = Path.GetFullPath(docPath);
                string dir = Path.GetDirectoryName(full);
                string root = Path.GetPathRoot(full);
                if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(root) || !Directory.Exists(dir))
                    return false;

                // Папка документа внутри корня — база для относительных путей картинок
                _mediaPrefix = dir.Substring(root.Length).TrimEnd('\\', '/').Replace('\\', '/');

                if (string.Equals(root, _mappedRoot, StringComparison.OrdinalIgnoreCase))
                    return false;

                CoreWebView2 core = _webView?.CoreWebView2;
                if (core == null)
                    return false;

                core.SetVirtualHostNameToFolderMapping(MediaHost, root,
                    CoreWebView2HostResourceAccessKind.Allow);
                _mappedRoot = root;
                Logger.Info("MapDocumentMedia: " + MediaHost + " -> " + root + " (doc dir: " + _mediaPrefix + ")");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("MapDocumentMedia", ex);
                return false;
            }
        }

        /// <summary>Базовый URL папки документа (для относительных путей картинок).</summary>
        private string ImageBaseUrl => "https://" + MediaHost + "/" +
            (string.IsNullOrEmpty(_mediaPrefix) ? "" : _mediaPrefix + "/");

        private List<Dictionary<string, object>> HeadingsToDto(List<Heading> headings)
        {
            var list = new List<Dictionary<string, object>>();
            if (headings == null)
                return list;
            foreach (Heading h in headings)
            {
                list.Add(new Dictionary<string, object>
                {
                    ["level"] = h.Level,
                    ["text"] = h.Text,
                    ["id"] = h.Id,
                });
            }
            return list;
        }

        private Dictionary<string, object> SettingsToDto()
        {
            return new Dictionary<string, object>
            {
                ["language"] = Loc.Lang,
                ["theme"] = _settings.Theme,
                ["codeTheme"] = _settings.CodeTheme,
                ["zoom"] = _settings.Zoom,
                ["readingWidth"] = _settings.ReadingWidth,
                ["showLineNumbers"] = _settings.ShowLineNumbers,
                ["showToc"] = _settings.ShowToc,
                ["splitView"] = _settings.SplitView,
            };
        }

        private static Dictionary<string, object> StatsToDto(DocumentStats stats)
        {
            return new Dictionary<string, object>
            {
                ["words"] = stats?.WordCount ?? 0,
                ["readingTime"] = stats?.ReadingTimeMinutes ?? 0,
                ["headings"] = stats?.HeadingCount ?? 0,
            };
        }

        // ---------------------------------------------------------------
        // Мост с js/app.js
        // ---------------------------------------------------------------

        private void Send(Dictionary<string, object> msg)
        {
            try
            {
                if (_webView?.CoreWebView2 != null)
                    _webView.CoreWebView2.PostWebMessageAsJson(Json.Serialize(msg));
            }
            catch (Exception ex)
            {
                Logger.Error("Send", ex);
            }
        }

        private void SendError(string text)
        {
            Send(new Dictionary<string, object> { ["type"] = "error", ["text"] = text });
        }

        private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string raw = e.TryGetWebMessageAsString();
                if (string.IsNullOrEmpty(raw))
                    raw = e.WebMessageAsJson;

                var msg = Json.DeserializeObject(raw) as Dictionary<string, object>;
                if (msg == null)
                    return;

                string type = msg.ContainsKey("type") ? msg["type"] as string : null;
                switch (type)
                {
                    case "ready":
                        _ready = true;
                        PushDocument();
                        break;

                    case "need-source":
                        // Страница включила split view — запрос исходника большого документа
                        SendSourceStream();
                        break;

                    case "settings-changed":
                        HandleSettingsChanged(msg);
                        break;

                    case "navigate":
                        NavigateRelative(msg.ContainsKey("href") ? msg["href"] as string : null);
                        break;

                    case "open-external":
                        OpenExternal(msg.ContainsKey("href") ? msg["href"] as string : null);
                        break;

                    case "print":
                        ShowPrint();
                        break;

                    case "export-pdf":
                        ExportPdf();
                        break;

                    case "context-menu":
                        // Правый клик / Shift+F10 перехвачен в app.js — событие
                        // ContextMenuRequested в WebView2 не всегда доходит до хоста
                        ShowPluginMenu();
                        break;

                    case "menu-dismiss":
                        // Клик / Escape / прокрутка в странице — закрыть меню
                        ClosePluginMenu();
                        break;

                    case "close":
                        // Escape в документе — закрыть окно просмотра (Lister)
                        CloseViewer();
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("OnWebMessageReceived", ex);
            }
        }

        private void HandleSettingsChanged(Dictionary<string, object> msg)
        {
            string key = msg.ContainsKey("key") ? msg["key"] as string : null;
            if (key == null)
                return;

            switch (key)
            {
                case "zoom":
                    _settings.Zoom = ToInt(msg["value"], _settings.Zoom);
                    break;
                case "showToc":
                    _settings.ShowToc = ToBool(msg["value"], _settings.ShowToc);
                    break;
                case "splitView":
                    _settings.SplitView = ToBool(msg["value"], _settings.SplitView);
                    break;
                case "showLineNumbers":
                    _settings.ShowLineNumbers = ToBool(msg["value"], _settings.ShowLineNumbers);
                    break;
                case "theme":
                    _settings.Theme = msg["value"] as string ?? _settings.Theme;
                    break;
                case "codeTheme":
                    _settings.CodeTheme = msg["value"] as string ?? _settings.CodeTheme;
                    break;
                case "readingWidth":
                    _settings.ReadingWidth = ToInt(msg["value"], _settings.ReadingWidth);
                    break;
            }

            try
            {
                _settings.Save();
            }
            catch (Exception ex)
            {
                Logger.Error("Save settings", ex);
            }

            UpdateMenuState();
        }

        private static int ToInt(object value, int fallback)
        {
            try
            {
                return Convert.ToInt32(value);
            }
            catch
            {
                return fallback;
            }
        }

        private static bool ToBool(object value, bool fallback)
        {
            if (value is bool b)
                return b;
            return fallback;
        }

        // ---------------------------------------------------------------
        // Навигация и ссылки
        // ---------------------------------------------------------------

        private void NavigateRelative(string href)
        {
            try
            {
                if (string.IsNullOrEmpty(href))
                    return;

                // Отрезаем якорь: other.md#section -> other.md
                string target = href;
                int hash = target.IndexOf('#');
                if (hash >= 0)
                    target = target.Substring(0, hash);
                if (target.Length == 0)
                    return;

                string baseDir = Path.GetDirectoryName(_currentFile ?? Plugin.PluginDir) ?? ".";
                string newPath = Path.GetFullPath(Path.Combine(baseDir,
                    target.Replace('/', Path.DirectorySeparatorChar)));

                if (File.Exists(newPath))
                    LoadFile(newPath);
                else
                    SendError(Loc.T("Err.FileNotFound") + newPath);
            }
            catch (Exception ex)
            {
                Logger.Error("NavigateRelative", ex);
                SendError(Loc.T("Err.Navigate") + ex.Message);
            }
        }

        private static void OpenExternal(string url)
        {
            try
            {
                if (!string.IsNullOrEmpty(url))
                    Process.Start(url); // ShellExecute по умолчанию
            }
            catch (Exception ex)
            {
                Logger.Error("OpenExternal", ex);
            }
        }

        // ---------------------------------------------------------------
        // Команды (от Lister и из меню)
        // ---------------------------------------------------------------

        /// <summary>Открывает панель поиска.</summary>
        public void OpenSearch()
        {
            Send(new Dictionary<string, object> { ["type"] = "find" });
        }

        /// <summary>Переходит к следующему/предыдущему совпадению поиска.</summary>
        /// <param name="forward">Направление: вперёд или назад.</param>
        public void FindNext(bool forward)
        {
            Send(new Dictionary<string, object>
            {
                ["type"] = forward ? "find-next" : "find-prev",
            });
        }

        /// <summary>Копирует выделенный текст в буфер обмена.</summary>
        public void CopySelection()
        {
            ExecuteScript("document.execCommand('copy');");
        }

        /// <summary>Выделяет всё содержимое документа.</summary>
        public void SelectAll()
        {
            ExecuteScript(
                "var s=window.getSelection();" +
                "s.removeAllRanges();" +
                "var r=document.createRange();" +
                "r.selectNodeContents(document.getElementById('content')||document.body);" +
                "s.addRange(r);");
        }

        /// <summary>Перезагружает настройки из INI и отправляет их в страницу.</summary>
        public void ReloadSettings()
        {
            try
            {
                _settings.Load();
                Loc.SetLanguage(_settings.Language);
                RebuildMenu();
                Send(new Dictionary<string, object>
                {
                    ["type"] = "settings",
                    ["settings"] = SettingsToDto(),
                });
                UpdateMenuState();
            }
            catch (Exception ex)
            {
                Logger.Error("ReloadSettings", ex);
            }
        }

        /// <summary>Показывает системный диалог печати.</summary>
        public void ShowPrint()
        {
            try
            {
                _webView?.CoreWebView2?.ShowPrintUI();
            }
            catch (Exception ex)
            {
                Logger.Error("ShowPrint", ex);
                SendError(Loc.T("Err.Print") + ex.Message);
            }
        }

        private void ExecuteScript(string script)
        {
            try
            {
                if (_webView?.CoreWebView2 != null)
                    _ = _webView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                Logger.Error("ExecuteScript", ex);
            }
        }

        private async void ExportPdf()
        {
            try
            {
                CoreWebView2 core = _webView?.CoreWebView2;
                if (core == null)
                    return;

                using (var dlg = new SaveFileDialog())
                {
                    dlg.Title = Loc.T("Pdf.Title");
                    dlg.Filter = Loc.T("Pdf.Filter");
                    dlg.FileName = (Path.GetFileNameWithoutExtension(_currentFile ?? "document") ?? "document") + ".pdf";
                    if (dlg.ShowDialog(this) != DialogResult.OK)
                        return;

                    bool ok = await core.PrintToPdfAsync(dlg.FileName, null);
                    if (!ok)
                        MessageBox.Show(this, Loc.T("Err.PdfFailed"),
                            "MarkdownLister", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("ExportPdf", ex);
                MessageBox.Show(this, Loc.T("Err.Pdf") + ex.Message,
                    "MarkdownLister", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ---------------------------------------------------------------
        // Контекстное меню
        // ---------------------------------------------------------------

        private void BuildContextMenu()
        {
            _menu = new ContextMenuStrip();

            _menu.Items.Add(Loc.T("Menu.Copy"), null, (s, e) => CopySelection());
            _menu.Items.Add(Loc.T("Menu.SelectAll"), null, (s, e) => SelectAll());
            _menu.Items.Add(new ToolStripSeparator());

            _menu.Items.Add(Loc.T("Menu.Search"), null, (s, e) => OpenSearch());
            _tocItem = new ToolStripMenuItem(Loc.T("Menu.Toc"), null, (s, e) => ToggleToc());
            _splitItem = new ToolStripMenuItem(Loc.T("Menu.Split"), null, (s, e) => ToggleSplit());
            _menu.Items.Add(_tocItem);
            _menu.Items.Add(_splitItem);
            _menu.Items.Add(new ToolStripSeparator());

            // Масштаб
            var zoom = new ToolStripMenuItem(Loc.T("Menu.Zoom"));
            zoom.DropDownItems.Add(Loc.T("Menu.ZoomIn"), null, (s, e) => ChangeZoom(+10));
            zoom.DropDownItems.Add(Loc.T("Menu.ZoomOut"), null, (s, e) => ChangeZoom(-10));
            zoom.DropDownItems.Add(Loc.T("Menu.ZoomReset"), null, (s, e) => SetZoom(100));
            _menu.Items.Add(zoom);

            // Тема
            var theme = new ToolStripMenuItem(Loc.T("Menu.Theme"));
            _themeSystem = new ToolStripMenuItem(Loc.T("Menu.ThemeSystem"), null, (s, e) => SetTheme("System"));
            _themeLight = new ToolStripMenuItem(Loc.T("Menu.ThemeLight"), null, (s, e) => SetTheme("Light"));
            _themeDark = new ToolStripMenuItem(Loc.T("Menu.ThemeDark"), null, (s, e) => SetTheme("Dark"));
            theme.DropDownItems.Add(_themeSystem);
            theme.DropDownItems.Add(_themeLight);
            theme.DropDownItems.Add(_themeDark);
            _menu.Items.Add(theme);

            // Язык
            var lang = new ToolStripMenuItem(Loc.T("Menu.Language"));
            _langAuto = new ToolStripMenuItem(Loc.T("Menu.LanguageAuto"), null, (s, e) => SetLanguage("Auto"));
            _langRu = new ToolStripMenuItem(Loc.T("Menu.LanguageRu"), null, (s, e) => SetLanguage("Ru"));
            _langEn = new ToolStripMenuItem(Loc.T("Menu.LanguageEn"), null, (s, e) => SetLanguage("En"));
            lang.DropDownItems.Add(_langAuto);
            lang.DropDownItems.Add(_langRu);
            lang.DropDownItems.Add(_langEn);
            _menu.Items.Add(lang);

            _lineNumbersItem = new ToolStripMenuItem(Loc.T("Menu.LineNumbers"), null,
                (s, e) => ToggleLineNumbers());
            _menu.Items.Add(_lineNumbersItem);
            _menu.Items.Add(new ToolStripSeparator());

            _menu.Items.Add(Loc.T("Menu.Print"), null, (s, e) => ShowPrint());
            _menu.Items.Add(Loc.T("Menu.ExportPdf"), null, (s, e) => ExportPdf());
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(Loc.T("Menu.Donate"), null, (s, e) => ShowDonate());
            _menu.Items.Add(Loc.T("Menu.About"), null, (s, e) => ShowAbout());

            _menu.Closed += OnMenuClosed;

            UpdateMenuState();
        }

        private void OnMenuClosed(object sender, ToolStripDropDownClosedEventArgs e)
        {
            // Страница должна знать, что меню закрыто: она перестаёт «гасить» клики
            Send(new Dictionary<string, object> { ["type"] = "menu-closed" });
        }

        private void OnContextMenuRequested(object sender, CoreWebView2ContextMenuRequestedEventArgs e)
        {
            try
            {
                e.Handled = true; // штатное меню отключено — показываем своё
                Logger.Info("ContextMenuRequested fired"); // диагностика: доходит ли событие
                ShowPluginMenu();
            }
            catch (Exception ex)
            {
                Logger.Error("ContextMenu", ex);
            }
        }

        /// <summary>Показывает контекстное меню плагина в позиции курсора.</summary>
        private void ShowPluginMenu()
        {
            try
            {
                if (_menu == null)
                    return;
                UpdateMenuState();
                _menu.Show(Cursor.Position);
                Send(new Dictionary<string, object> { ["type"] = "menu-opened" });
            }
            catch (Exception ex)
            {
                Logger.Error("ShowPluginMenu", ex);
            }
        }

        /// <summary>Закрывает контекстное меню плагина, если оно показано.</summary>
        private void ClosePluginMenu()
        {
            try
            {
                if (_menu != null && _menu.Visible)
                    _menu.Close(ToolStripDropDownCloseReason.CloseCalled);
            }
            catch (Exception ex)
            {
                Logger.Error("ClosePluginMenu", ex);
            }
        }

        /// <summary>
        /// Закрывает окно просмотра (Lister) по Escape. В Quick View плагин встроен
        /// в главное окно Total Commander — там ничего не делаем, чтобы не закрыть TC.
        /// </summary>
        private void CloseViewer()
        {
            try
            {
                // Верхнее окно цепочки родителей (первое без стиля WS_CHILD):
                // в режиме F3 это окно Lister, в Quick View — главное окно TC.
                // Имя класса окна Lister не используем: оно зависит от версии TC.
                IntPtr top = Handle;
                var chain = new StringBuilder();
                for (IntPtr hwnd = Handle; hwnd != IntPtr.Zero; hwnd = NativeMethods.GetParent(hwnd))
                {
                    var cls = new StringBuilder(256);
                    NativeMethods.GetClassName(hwnd, cls, cls.Capacity);
                    if (chain.Length > 0)
                        chain.Append(" <- ");
                    chain.Append(cls);

                    if ((NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE) & NativeMethods.WS_CHILD) == 0)
                    {
                        top = hwnd;
                        break;
                    }
                }
                Logger.Info("CloseViewer: chain " + chain);

                var topCls = new StringBuilder(256);
                NativeMethods.GetClassName(top, topCls, topCls.Capacity);

                // Quick View: плагин встроен в главное окно Total Commander — не закрываем
                if (string.Equals(topCls.ToString(), "TTOTAL_CMD", StringComparison.OrdinalIgnoreCase))
                    return;

                NativeMethods.PostMessage(top, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Logger.Error("CloseViewer", ex);
            }
        }

        private void UpdateMenuState()
        {
            if (_menu == null)
                return;

            _tocItem.Checked = _settings.ShowToc;
            _splitItem.Checked = _settings.SplitView;
            _lineNumbersItem.Checked = _settings.ShowLineNumbers;

            string theme = _settings.Theme ?? "System";
            _themeSystem.Checked = theme == "System";
            _themeLight.Checked = theme == "Light";
            _themeDark.Checked = theme == "Dark";

            string lang = _settings.Language ?? "Auto";
            _langAuto.Checked = lang.Equals("Auto", StringComparison.OrdinalIgnoreCase);
            _langRu.Checked = lang.Equals("Ru", StringComparison.OrdinalIgnoreCase);
            _langEn.Checked = lang.Equals("En", StringComparison.OrdinalIgnoreCase);
        }

        private void ToggleToc()
        {
            _settings.ShowToc = !_settings.ShowToc;
            _settings.Save();
            Send(new Dictionary<string, object>
            {
                ["type"] = "settings",
                ["settings"] = SettingsToDto(),
            });
            UpdateMenuState();
        }

        private void ToggleSplit()
        {
            _settings.SplitView = !_settings.SplitView;
            _settings.Save();
            Send(new Dictionary<string, object> { ["type"] = "toggle-split" });
            UpdateMenuState();
        }

        private void ToggleLineNumbers()
        {
            _settings.ShowLineNumbers = !_settings.ShowLineNumbers;
            _settings.Save();
            Send(new Dictionary<string, object>
            {
                ["type"] = "settings",
                ["settings"] = SettingsToDto(),
            });
            UpdateMenuState();
        }

        private void ChangeZoom(int delta)
        {
            SetZoom(_settings.Zoom + delta);
        }

        private void SetZoom(int value)
        {
            _settings.Zoom = Math.Min(200, Math.Max(50, value));
            _settings.Save();
            Send(new Dictionary<string, object>
            {
                ["type"] = "set-zoom",
                ["value"] = _settings.Zoom,
            });
        }

        private void SetTheme(string theme)
        {
            _settings.Theme = theme;
            _settings.Save();
            Send(new Dictionary<string, object>
            {
                ["type"] = "set-theme",
                ["value"] = theme,
            });
            UpdateMenuState();
        }

        /// <summary>
        /// Переключает язык интерфейса: сохраняет настройку, перестраивает меню
        /// и отправляет странице обновлённые настройки (language в SettingsToDto).
        /// </summary>
        /// <param name="value">"Auto", "Ru" или "En".</param>
        private void SetLanguage(string value)
        {
            _settings.Language = value;
            _settings.Save();
            Loc.SetLanguage(value);
            RebuildMenu();
            Send(new Dictionary<string, object>
            {
                ["type"] = "settings",
                ["settings"] = SettingsToDto(),
            });
        }

        /// <summary>Перестраивает контекстное меню (после смены языка).</summary>
        private void RebuildMenu()
        {
            var old = _menu;
            BuildContextMenu();
            try { old?.Dispose(); } catch { /* старое меню не должно мешать */ }
        }

        private void ShowAbout()
        {
            try
            {
                Version ver = typeof(ListerControl).Assembly.GetName().Version;
                string text = Loc.T("About.Text", ver?.ToString(3) ?? "0.1.0");

                MessageBox.Show(this, text, Loc.T("About.Title"),
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Logger.Error("ShowAbout", ex);
            }
        }

        /// <summary>Показывает диалог «Поддержать проект» с реквизитами доната.</summary>
        private void ShowDonate()
        {
            try
            {
                using (var dlg = new DonateDialog())
                    dlg.ShowDialog(this);
            }
            catch (Exception ex)
            {
                Logger.Error("ShowDonate", ex);
            }
        }

        // ---------------------------------------------------------------
        // Освобождение ресурсов
        // ---------------------------------------------------------------

        /// <summary>Освобождает ресурсы WebView2 и меню.</summary>
        /// <param name="disposing">true — управляемые ресурсы.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    _loadCts?.Cancel();
                    if (_webView != null)
                    {
                        if (_webView.CoreWebView2 != null)
                        {
                            _webView.CoreWebView2.ContextMenuRequested -= OnContextMenuRequested;
                            _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
                        }
                        _webView.Dispose();
                    }
                    _menu?.Dispose();
                    _fallback?.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.Error("Dispose", ex);
                }
            }
            base.Dispose(disposing);
        }
    }
}
