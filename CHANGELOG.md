# История изменений

Формат основан на [Keep a Changelog](https://keepachangelog.com/ru/1.1.0/),
нумерация версий — [Semantic Versioning](https://semver.org/lang/ru/).

## [0.2.0] - 2026-11-03

### Добавлено

- Контекстное меню плагина: правый клик по документу или Shift+F10 — темы,
  зум, split view, оглавление, печать, «Поддержать проект», «О плагине».
- Диалог «Поддержать проект» и секция доната в README (YooMoney, WebMoney, IBAN).
- Кнопка **Sponsor** на GitHub (`.github/FUNDING.yml`).
- Ленивая загрузка Mermaid и KaTeX: тяжёлые библиотеки подключаются только
  если в документе есть диаграммы или формулы — обычные файлы открываются быстрее.
- `CHANGELOG.md` — история изменений.
- Автосборка на GitHub Actions (`.github/workflows/build.yml`): сборка x86/x64
  и релизного архива при пуше и PR; по тегу `v*` — публикация GitHub Release.

### Исправлено

- Локальные изображения документа: относительные пути (`docs/images/…`) теперь
  отображаются в Lister — папка документа раздаётся плагином отдельным хостом.
- Оглавление: корректно собираются заголовки уровней 2–6.
- Рендеринг диаграмм Mermaid.

### Документация

- README: hero-блок, бейджи версии и лицензии, скриншоты, содержание.

## [0.1.4] - 2026-10-30

Первый релиз. WLX-плагин Total Commander: просмотр Markdown-файлов в стиле
GitHub по **F3** и в Quick View (Ctrl+Q).

### Добавлено

- Рендеринг Markdig (GFM: таблицы, сноски, списки задач, YAML front matter) в WebView2.
- Split view «исходник | результат» с синхронизацией прокрутки (Ctrl+M).
- Диаграммы Mermaid, формулы KaTeX, подсветка кода (highlight.js).
- Оглавление с переходами по якорям, поиск (Ctrl+F), навигация.
- Темы: System / Light / Dark + JSON-пресеты в `themes\`.
- Зум (Ctrl+колесо), ширина чтения, печать (Ctrl+P).
- Настройки в `MarkdownLister.WLX.ini` рядом с плагином; без телеметрии
  и сетевых запросов.

[0.2.0]: https://github.com/StarLeG/tc-markdown-lister/compare/v0.1.4...v0.2.0
[0.1.4]: https://github.com/StarLeG/tc-markdown-lister/releases/tag/v0.1.4
