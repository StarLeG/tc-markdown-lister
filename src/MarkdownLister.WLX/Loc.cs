// Автор: Старинский Олег Григорьевич (info@electroman.by, @ElectroManBY)
// MarkdownLister: строки интерфейса (ru/en) и выбор активного языка.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace MarkdownLister.WLX
{
    /// <summary>
    /// Локализация строк интерфейса плагина (русский / английский).
    /// Активный язык: "ru" или "en"; настройка пользователя — Auto / Ru / En
    /// (Auto — по языку интерфейса ОС). Строки страницы (js/app.js) локализуются
    /// отдельно: эффективный язык передаётся в настройках страницы.
    /// </summary>
    internal static class Loc
    {
        /// <summary>Эффективный язык: "ru" или "en".</summary>
        public static string Lang { get; private set; } = "ru";

        /// <summary>
        /// Устанавливает язык по пользовательской настройке.
        /// </summary>
        /// <param name="setting">"Ru", "En" или "Auto" (пусто — как Auto).</param>
        public static void SetLanguage(string setting)
        {
            if (string.Equals(setting, "En", StringComparison.OrdinalIgnoreCase))
                Lang = "en";
            else if (string.Equals(setting, "Ru", StringComparison.OrdinalIgnoreCase))
                Lang = "ru";
            else
                Lang = string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
                    "ru", StringComparison.OrdinalIgnoreCase) ? "ru" : "en";
        }

        /// <summary>Возвращает строку на активном языке (или сам ключ, если строки нет).</summary>
        /// <param name="key">Ключ строки.</param>
        public static string T(string key)
        {
            var table = Lang == "en" ? En : Ru;
            return table.TryGetValue(key, out var s) ? s : (Ru.TryGetValue(key, out var r) ? r : key);
        }

        /// <summary>Возвращает строку на активном языке с подстановкой аргументов ({0}, {1}, …).</summary>
        /// <param name="key">Ключ строки.</param>
        /// <param name="args">Аргументы форматирования.</param>
        public static string T(string key, params object[] args)
            => string.Format(T(key), args);

        // ---------------------------------------------------------------
        // Строки: ru / en
        // ---------------------------------------------------------------

        private static readonly Dictionary<string, string> Ru = new Dictionary<string, string>
        {
            // Контекстное меню
            ["Menu.Copy"] = "Копировать",
            ["Menu.SelectAll"] = "Выделить всё",
            ["Menu.Search"] = "Поиск…\tCtrl+F",
            ["Menu.Toc"] = "Оглавление\tCtrl+O",
            ["Menu.Split"] = "Split view\tCtrl+M",
            ["Menu.Zoom"] = "Масштаб",
            ["Menu.ZoomIn"] = "Увеличить\tCtrl++",
            ["Menu.ZoomOut"] = "Уменьшить\tCtrl+-",
            ["Menu.ZoomReset"] = "Сбросить\tCtrl+0",
            ["Menu.Theme"] = "Тема",
            ["Menu.ThemeSystem"] = "Системная",
            ["Menu.ThemeLight"] = "Светлая",
            ["Menu.ThemeDark"] = "Тёмная",
            ["Menu.Language"] = "Язык / Language",
            ["Menu.LanguageAuto"] = "Авто (по языку ОС)",
            ["Menu.LanguageRu"] = "Русский",
            ["Menu.LanguageEn"] = "English",
            ["Menu.LineNumbers"] = "Номера строк в коде",
            ["Menu.Print"] = "Печать…\tCtrl+P",
            ["Menu.ExportPdf"] = "Экспорт в PDF…",
            ["Menu.Donate"] = "Поддержать проект…",
            ["Menu.About"] = "О плагине…",

            // Ошибки и служебные сообщения
            ["Err.WebView2"] =
                "Не удалось инициализировать WebView2.\n\n" +
                "Вероятно, на компьютере отсутствует Microsoft Edge WebView2 Runtime.\n" +
                "Скачайте его: https://developer.microsoft.com/microsoft-edge/webview2/\n\n" +
                "Подробности: {0}",
            ["Err.FileNotFound"] = "Файл не найден: ",
            ["Err.LoadFile"] = "Ошибка загрузки файла: ",
            ["Err.Navigate"] = "Ошибка перехода по ссылке: ",
            ["Err.Print"] = "Не удалось открыть диалог печати: ",
            ["Err.PdfFailed"] = "Не удалось экспортировать PDF.",
            ["Err.Pdf"] = "Ошибка экспорта PDF: ",
            ["Pdf.Title"] = "Экспорт в PDF",
            ["Pdf.Filter"] = "Документ PDF (*.pdf)|*.pdf",

            // Предупреждение о большом файле
            ["LargeFile.Text"] =
                "Файл «{0}» имеет размер {1:F1} МБ (порог предупреждения — {2} МБ).\n\nОткрыть его?",

            // О плагине
            ["About.Title"] = "О плагине — MarkdownLister",
            ["About.Text"] =
                "MarkdownLister — WLX-плагин для Total Commander\n" +
                "Удобный просмотр Markdown-файлов\n\n" +
                "Версия: {0}\n\n" +
                "Автор: Старинский Олег Григорьевич\n" +
                "Email: info@electroman.by\n" +
                "Telegram: @ElectroManBY\n\n" +
                "Поддержать проект: контекстное меню → «Поддержать проект…»\n\n" +
                "Лицензия: MIT\n" +
                "https://github.com/StarLeG/tc-markdown-lister",

            // Диалог «Поддержать проект»
            ["Donate.Title"] = "Поддержать проект — MarkdownLister",
            ["Donate.Close"] = "Закрыть",
            ["Donate.Copy"] = "Копировать",
            ["Donate.Copied"] = "Скопировано",
            ["Donate.Intro"] =
                "Любая сумма, за минуту. Фиксированного прайса нет: сумму и повод " +
                "выбираете сами. Удобнее всего с телефона.",
            ["Donate.YooMoney.Text"] = "Картой любого банка, без регистрации.",
            ["Donate.YooMoney.Wallet"] = "Кошелёк: {0}",
            ["Donate.YooMoney.Link"] = "Перевести через ЮMoney →",
            ["Donate.WebMoney.Text"] = "Страница доната или бот в Telegram.",
            ["Donate.WebMoney.Page"] = "Страница доната →",
            ["Donate.WebMoney.Bot"] = "Бот WebMoney в Telegram →",
            ["Donate.Iban.Header"] = "Пополнение счёта по IBAN (Беларусь)",
            ["Donate.Iban.Text1"] =
                "Из интернет-банка или приложения другого банка, не Сбер Банка, — " +
                "оплатой через ЕРИП:",
            ["Donate.Iban.Path"] =
                "ЕРИП → Банковские, финансовые услуги → Банки, НКО и НФО → Сбер Банк → " +
                "Пополнение счёта картой",
            ["Donate.Iban.Account"] = "Номер счёта: {0}",
            ["Donate.Iban.SberPhone"] =
                "Клиентам Сбер Банка — перевод по номеру телефона: {0}",
            ["Donate.Iban.Verify"] =
                "Перед оплатой система покажет имя получателя — проверьте его.",
            ["Donate.Sber.Header"] = "СберБанк (Россия) — по номеру телефона",
            ["Donate.Sber.Text"] =
                "В СберБанк Онлайн: перевод в Беларусь по номеру телефона, получатель — " +
                "клиент Сбер Банка (Беларусь).",
            ["Donate.Sber.Number"] = "Номер: {0}",
            ["Donate.Sber.Verify"] =
                "Перед переводом приложение покажет имя получателя — проверьте его.",
            ["Donate.NotOnly"] =
                "Не только деньгами: отчёт о баге или рассказ коллегам на форуме " +
                "помогают проекту не меньше.",
        };

        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            // Context menu
            ["Menu.Copy"] = "Copy",
            ["Menu.SelectAll"] = "Select all",
            ["Menu.Search"] = "Find…\tCtrl+F",
            ["Menu.Toc"] = "Contents\tCtrl+O",
            ["Menu.Split"] = "Split view\tCtrl+M",
            ["Menu.Zoom"] = "Zoom",
            ["Menu.ZoomIn"] = "Zoom in\tCtrl++",
            ["Menu.ZoomOut"] = "Zoom out\tCtrl+-",
            ["Menu.ZoomReset"] = "Reset\tCtrl+0",
            ["Menu.Theme"] = "Theme",
            ["Menu.ThemeSystem"] = "System",
            ["Menu.ThemeLight"] = "Light",
            ["Menu.ThemeDark"] = "Dark",
            ["Menu.Language"] = "Язык / Language",
            ["Menu.LanguageAuto"] = "Auto (system language)",
            ["Menu.LanguageRu"] = "Русский",
            ["Menu.LanguageEn"] = "English",
            ["Menu.LineNumbers"] = "Line numbers in code",
            ["Menu.Print"] = "Print…\tCtrl+P",
            ["Menu.ExportPdf"] = "Export to PDF…",
            ["Menu.Donate"] = "Support the project…",
            ["Menu.About"] = "About…",

            // Errors and service messages
            ["Err.WebView2"] =
                "Could not initialize WebView2.\n\n" +
                "Microsoft Edge WebView2 Runtime is probably not installed.\n" +
                "Download it: https://developer.microsoft.com/microsoft-edge/webview2/\n\n" +
                "Details: {0}",
            ["Err.FileNotFound"] = "File not found: ",
            ["Err.LoadFile"] = "Error loading file: ",
            ["Err.Navigate"] = "Error following link: ",
            ["Err.Print"] = "Could not open the print dialog: ",
            ["Err.PdfFailed"] = "Could not export PDF.",
            ["Err.Pdf"] = "PDF export error: ",
            ["Pdf.Title"] = "Export to PDF",
            ["Pdf.Filter"] = "PDF document (*.pdf)|*.pdf",

            // Large file warning
            ["LargeFile.Text"] =
                "The file \"{0}\" is {1:F1} MB (warning threshold is {2} MB).\n\nOpen it?",

            // About
            ["About.Title"] = "About — MarkdownLister",
            ["About.Text"] =
                "MarkdownLister — a WLX plugin for Total Commander\n" +
                "A convenient Markdown file viewer\n\n" +
                "Version: {0}\n\n" +
                "Author: Старинский Олег Григорьевич\n" +
                "Email: info@electroman.by\n" +
                "Telegram: @ElectroManBY\n\n" +
                "Support the project: context menu → \"Support the project…\"\n\n" +
                "License: MIT\n" +
                "https://github.com/StarLeG/tc-markdown-lister",

            // Donate dialog
            ["Donate.Title"] = "Support the project — MarkdownLister",
            ["Donate.Close"] = "Close",
            ["Donate.Copy"] = "Copy",
            ["Donate.Copied"] = "Copied",
            ["Donate.Intro"] =
                "Any amount, in a minute. There is no fixed price: you choose the amount " +
                "and the occasion. Easiest from a phone.",
            ["Donate.YooMoney.Text"] = "Any bank card, no sign-up required.",
            ["Donate.YooMoney.Wallet"] = "Wallet: {0}",
            ["Donate.YooMoney.Link"] = "Donate via YooMoney →",
            ["Donate.WebMoney.Text"] = "Donation page or a Telegram bot.",
            ["Donate.WebMoney.Page"] = "Donation page →",
            ["Donate.WebMoney.Bot"] = "WebMoney bot in Telegram →",
            ["Donate.Iban.Header"] = "IBAN top-up (Belarus)",
            ["Donate.Iban.Text1"] =
                "From another bank's online banking or app (not Sber Bank) — " +
                "pay via ERIP (ЕРИП):",
            ["Donate.Iban.Path"] =
                "ЕРИП → Банковские, финансовые услуги → Банки, НКО и НФО → Сбер Банк → " +
                "Пополнение счёта картой",
            ["Donate.Iban.Account"] = "Account: {0}",
            ["Donate.Iban.SberPhone"] =
                "Sber Bank clients — transfer by phone number: {0}",
            ["Donate.Iban.Verify"] =
                "Before payment the system will show the recipient's name — please verify it.",
            ["Donate.Sber.Header"] = "SberBank (Russia) — by phone number",
            ["Donate.Sber.Text"] =
                "In SberBank Online: transfer to Belarus by phone number; the recipient is " +
                "a Sber Bank (Belarus) client.",
            ["Donate.Sber.Number"] = "Number: {0}",
            ["Donate.Sber.Verify"] =
                "Before the transfer the app will show the recipient's name — please verify it.",
            ["Donate.NotOnly"] =
                "Not only with money: a bug report or telling colleagues on a forum " +
                "helps the project just as much.",
        };
    }
}
