// Автор: Старинский Олег Григорьевич (info@electroman.by, @ElectroManBY)
// MarkdownLister: диалог «Поддержать проект» — реквизиты доната, ссылки, копирование.

using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace MarkdownLister.WLX
{
    /// <summary>
    /// Диалог «Поддержать проект»: реквизиты (ЮMoney, WebMoney, IBAN, СберБанк),
    /// кликабельные ссылки на платёжные сервисы и копирование реквизитов в буфер обмена.
    /// </summary>
    internal sealed class DonateDialog : Form
    {
        private const string YooMoneyUrl = "https://yoomoney.ru/to/4100119514684342";
        private const string WebMoneyUrl = "https://pay.web.money/d/1kji";
        private const string WebMoneyBotUrl = "https://t.me/WebMoneyWalletBot/donate?startapp=1kji";

        private readonly Font _boldFont;

        /// <summary>Создаёт диалог с реквизитами поддержки проекта.</summary>
        public DonateDialog()
        {
            _boldFont = new Font(Font, FontStyle.Bold);

            Text = Loc.T("Donate.Title");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(560, 600);

            // Прокручиваемая область с реквизитами
            var scroll = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(14, 12, 14, 8),
            };
            var flow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
            };
            scroll.Controls.Add(flow);

            var closeButton = new Button
            {
                Text = Loc.T("Donate.Close"),
                DialogResult = DialogResult.OK,
                Dock = DockStyle.Bottom,
                Height = 34,
            };
            AcceptButton = closeButton;
            CancelButton = closeButton;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(scroll, 0, 0);
            layout.Controls.Add(closeButton, 0, 1);
            Controls.Add(layout);

            BuildContent(flow);
        }

        /// <summary>Освобождает ресурсы формы (шрифт заголовков).</summary>
        /// <param name="disposing">true — управляемые ресурсы.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && _boldFont != null)
                _boldFont.Dispose();
            base.Dispose(disposing);
        }

        // ---------------------------------------------------------------
        // Построение содержимого
        // ---------------------------------------------------------------

        private void BuildContent(FlowLayoutPanel flow)
        {
            flow.Controls.Add(MakeText(Loc.T("Donate.Intro")));

            // ЮMoney
            flow.Controls.Add(MakeHeader("ЮMoney"));
            flow.Controls.Add(MakeText(Loc.T("Donate.YooMoney.Text")));
            flow.Controls.Add(MakeValue(Loc.T("Donate.YooMoney.Wallet", "4100119514684342"), "4100119514684342"));
            flow.Controls.Add(MakeLink(Loc.T("Donate.YooMoney.Link"), YooMoneyUrl));

            // WebMoney
            flow.Controls.Add(MakeHeader("WebMoney"));
            flow.Controls.Add(MakeText(Loc.T("Donate.WebMoney.Text")));
            flow.Controls.Add(MakeLink(Loc.T("Donate.WebMoney.Page"), WebMoneyUrl));
            flow.Controls.Add(MakeLink(Loc.T("Donate.WebMoney.Bot"), WebMoneyBotUrl));

            // IBAN (Беларусь)
            flow.Controls.Add(MakeHeader(Loc.T("Donate.Iban.Header")));
            flow.Controls.Add(MakeText(Loc.T("Donate.Iban.Text1")));
            // Путь в меню ЕРИП повторяет пункты банковского интерфейса (они на русском)
            // и не переводится.
            flow.Controls.Add(MakeText(Loc.T("Donate.Iban.Path"), small: true));
            flow.Controls.Add(MakeValue(
                Loc.T("Donate.Iban.Account", "BY18 BPSB 3014 F000 0000 1164 7609"),
                "BY18BPSB3014F000000011647609"));
            flow.Controls.Add(MakeValue(
                Loc.T("Donate.Iban.SberPhone", "+375 29 714‑28‑82"),
                "+375297142882"));
            flow.Controls.Add(MakeText(Loc.T("Donate.Iban.Verify"), small: true));

            // СберБанк (Россия)
            flow.Controls.Add(MakeHeader(Loc.T("Donate.Sber.Header")));
            flow.Controls.Add(MakeText(Loc.T("Donate.Sber.Text")));
            flow.Controls.Add(MakeValue(Loc.T("Donate.Sber.Number", "+375 29 714‑28‑82"), "+375297142882"));
            flow.Controls.Add(MakeText(Loc.T("Donate.Sber.Verify"), small: true));

            // Не только деньгами
            flow.Controls.Add(MakeText(Loc.T("Donate.NotOnly"), small: true));
        }

        private Control MakeHeader(string text)
        {
            var label = new Label
            {
                Text = text,
                Font = _boldFont,
                AutoSize = true,
                Margin = new Padding(0, 12, 0, 2),
            };
            return label;
        }

        private Control MakeText(string text, bool small = false)
        {
            var label = new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(510, 0),
                Margin = new Padding(0, 2, 0, 2),
            };
            if (small)
                label.ForeColor = SystemColors.GrayText;
            return label;
        }

        /// <summary>Строка с реквизитом и кнопкой «Копировать».</summary>
        /// <param name="text">Отображаемая строка (подпись + значение).</param>
        /// <param name="copyValue">Значение, копируемое в буфер обмена.</param>
        private Control MakeValue(string text, string copyValue)
        {
            var row = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 2, 0, 2),
            };
            var label = new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(430, 0),
                Padding = new Padding(0, 6, 0, 0),
            };
            var button = new Button
            {
                Text = Loc.T("Donate.Copy"),
                AutoSize = true,
                Margin = new Padding(8, 0, 0, 0),
            };
            button.Click += (s, e) => CopyToClipboard(copyValue, button);
            row.Controls.Add(label);
            row.Controls.Add(button);
            return row;
        }

        private Control MakeLink(string text, string url)
        {
            var link = new LinkLabel
            {
                Text = text,
                AutoSize = true,
                Margin = new Padding(0, 2, 0, 2),
            };
            link.LinkClicked += (s, e) => OpenUrl(url);
            return link;
        }

        // ---------------------------------------------------------------
        // Буфер обмена и ссылки
        // ---------------------------------------------------------------

        private static void CopyToClipboard(string value, Button button)
        {
            try
            {
                Clipboard.SetText(value);
                button.Text = Loc.T("Donate.Copied");
                var timer = new Timer { Interval = 1500 };
                timer.Tick += (s, e) =>
                {
                    button.Text = Loc.T("Donate.Copy");
                    timer.Stop();
                    timer.Dispose();
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                Logger.Error("DonateDialog.Copy", ex);
            }
        }

        private static void OpenUrl(string url)
        {
            try
            {
                if (!string.IsNullOrEmpty(url))
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.Error("DonateDialog.OpenUrl", ex);
            }
        }
    }
}
