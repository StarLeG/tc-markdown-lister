// Автор: Старинский Олег Григорьевич (info@electroman.by, @ElectroManBY)
// WLX-плагин MarkdownLister: окно просмотра на базе WebView2 и мост к js/app.js.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
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

        private readonly PluginSettings _settings;
        private readonly MarkdownRenderer _renderer = new MarkdownRenderer();

        private WebView2 _webView;
        private Label _fallback;
        private ContextMenuStrip _menu;

        private string _currentFile;
        private bool _ready;

        private string _pendingHtml;
        private string _pendingSource;
        private List<Heading> _pendingHeadings;
        private DocumentStats _pendingStats;

        private ToolStripMenuItem _themeSystem, _themeLight, _themeDark;
        private ToolStripMenuItem _tocItem, _splitItem, _lineNumbersItem;

        /// <summary>Создаёт контрол с заданными настройками.</summary>
        /// <param name="settings">Настройки плагина.</param>
        public ListerControl(PluginSettings settings)
        {
            _settings = settings ?? new PluginSettings(Path.Combine(Bootstrap.PluginDir, "MarkdownLister.WLX.dll"));

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = SystemColors.Window;

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

                core.Navigate($"https://{VirtualHost}/template.html");
            }
            catch (Exception ex)
            {
                Logger.Error("InitWebView", ex);
                ShowFallback(
                    "Не удалось инициализировать WebView2.\n\n" +
                    "Вероятно, на компьютере отсутствует Microsoft Edge WebView2 Runtime.\n" +
                    "Скачайте его: https://developer.microsoft.com/microsoft-edge/webview2/\n\n" +
                    "Подробности: " + ex.Message);
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

        /// <summary>Читает Markdown-файл и отправляет его в страницу.</summary>
        /// <param name="path">Путь к файлу.</param>
        public void LoadFile(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    SendError("Файл не найден: " + path);
                    return;
                }

                byte[] bytes = File.ReadAllBytes(path);
                string text = EncodingDetector.Decode(bytes, out _);

                _currentFile = path;
                _pendingSource = text;
                _pendingHtml = _renderer.ToHtml(text);
                _pendingHeadings = _renderer.ExtractHeadings(text);
                _pendingStats = DocumentStats.Compute(text);

                PushDocument();
            }
            catch (Exception ex)
            {
                Logger.Error("LoadFile", ex);
                SendError("Ошибка загрузки файла: " + ex.Message);
            }
        }

        private void PushDocument()
        {
            if (!_ready || _pendingHtml == null)
                return;

            var msg = new Dictionary<string, object>
            {
                ["type"] = "load",
                ["html"] = _pendingHtml,
                ["source"] = _pendingSource,
                ["headings"] = HeadingsToDto(_pendingHeadings),
                ["settings"] = SettingsToDto(),
                ["stats"] = StatsToDto(_pendingStats),
            };
            Send(msg);
        }

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
                    SendError("Файл не найден: " + newPath);
            }
            catch (Exception ex)
            {
                Logger.Error("NavigateRelative", ex);
                SendError("Ошибка перехода по ссылке: " + ex.Message);
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
                SendError("Не удалось открыть диалог печати: " + ex.Message);
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
                    dlg.Title = "Экспорт в PDF";
                    dlg.Filter = "Документ PDF (*.pdf)|*.pdf";
                    dlg.FileName = (Path.GetFileNameWithoutExtension(_currentFile ?? "document") ?? "document") + ".pdf";
                    if (dlg.ShowDialog(this) != DialogResult.OK)
                        return;

                    bool ok = await core.PrintToPdfAsync(dlg.FileName, null);
                    if (!ok)
                        MessageBox.Show(this, "Не удалось экспортировать PDF.",
                            "MarkdownLister", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("ExportPdf", ex);
                MessageBox.Show(this, "Ошибка экспорта PDF: " + ex.Message,
                    "MarkdownLister", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ---------------------------------------------------------------
        // Контекстное меню
        // ---------------------------------------------------------------

        private void BuildContextMenu()
        {
            _menu = new ContextMenuStrip();

            _menu.Items.Add("Копировать", null, (s, e) => CopySelection());
            _menu.Items.Add("Выделить всё", null, (s, e) => SelectAll());
            _menu.Items.Add(new ToolStripSeparator());

            _menu.Items.Add("Поиск…\tCtrl+F", null, (s, e) => OpenSearch());
            _tocItem = new ToolStripMenuItem("Оглавление\tCtrl+O", null, (s, e) => ToggleToc());
            _splitItem = new ToolStripMenuItem("Split view\tCtrl+M", null, (s, e) => ToggleSplit());
            _menu.Items.Add(_tocItem);
            _menu.Items.Add(_splitItem);
            _menu.Items.Add(new ToolStripSeparator());

            // Масштаб
            var zoom = new ToolStripMenuItem("Масштаб");
            zoom.DropDownItems.Add("Увеличить\tCtrl++", null, (s, e) => ChangeZoom(+10));
            zoom.DropDownItems.Add("Уменьшить\tCtrl+-", null, (s, e) => ChangeZoom(-10));
            zoom.DropDownItems.Add("Сбросить\tCtrl+0", null, (s, e) => SetZoom(100));
            _menu.Items.Add(zoom);

            // Тема
            var theme = new ToolStripMenuItem("Тема");
            _themeSystem = new ToolStripMenuItem("Системная", null, (s, e) => SetTheme("System"));
            _themeLight = new ToolStripMenuItem("Светлая", null, (s, e) => SetTheme("Light"));
            _themeDark = new ToolStripMenuItem("Тёмная", null, (s, e) => SetTheme("Dark"));
            theme.DropDownItems.Add(_themeSystem);
            theme.DropDownItems.Add(_themeLight);
            theme.DropDownItems.Add(_themeDark);
            _menu.Items.Add(theme);

            _lineNumbersItem = new ToolStripMenuItem("Номера строк в коде", null,
                (s, e) => ToggleLineNumbers());
            _menu.Items.Add(_lineNumbersItem);
            _menu.Items.Add(new ToolStripSeparator());

            _menu.Items.Add("Печать…\tCtrl+P", null, (s, e) => ShowPrint());
            _menu.Items.Add("Экспорт в PDF…", null, (s, e) => ExportPdf());
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add("Поддержать проект…", null, (s, e) => ShowDonate());
            _menu.Items.Add("О плагине…", null, (s, e) => ShowAbout());

            UpdateMenuState();
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
            }
            catch (Exception ex)
            {
                Logger.Error("ShowPluginMenu", ex);
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

        private void ShowAbout()
        {
            try
            {
                Version ver = typeof(ListerControl).Assembly.GetName().Version;
                string text =
                    "MarkdownLister — WLX-плагин для Total Commander\n" +
                    "Удобный просмотр Markdown-файлов\n\n" +
                    $"Версия: {ver?.ToString(3) ?? "0.1.0"}\n\n" +
                    "Автор: Старинский Олег Григорьевич\n" +
                    "Email: info@electroman.by\n" +
                    "Telegram: @ElectroManBY\n\n" +
                    "Поддержать проект: контекстное меню → «Поддержать проект…»\n\n" +
                    "Лицензия: MIT\n" +
                    "https://github.com/StarLeG/tc-markdown-lister";

                MessageBox.Show(this, text, "О плагине — MarkdownLister",
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
