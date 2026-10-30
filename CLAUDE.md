# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Обзор

Проект «MarkdownLister» — WLX-плагин (Lister) для Total Commander: удобный просмотр Markdown-файлов (ренддеринг в стиле GitHub, split view, диаграммы, формулы, темы, поиск, навигация). Репозиторий `MarkdownViewer`.

Полное ТЗ — локальный файл `promt.md` (в git не включён, см. `.gitignore`); это авторитетный источник требований, при расхождении с ним приоритет у ТЗ.

Стек — C# / .NET / WebView2, **не Qt**, несмотря на путь `d:\Qt\QtProject`.

## Стек и ключевые решения

- C# / .NET Framework 4.8, multi-target x86/x64; артефакт сборки — `.wlx`.
- Рендеринг — WebView2 (Edge Chromium); асинхронная инициализация, без блокировки UI-потока.
- Парсинг Markdown — Markdig (GFM, YAML front matter, сноски, списки задач).
- Подсветка кода — Prism.js или highlight.js; диаграммы — Mermaid.js; формулы — KaTeX; санитизация HTML — DOMPurify (всё встроено в HTML-шаблон).
- Настройки — INI-файл рядом с плагином; без телеметрии и сетевых запросов.

## Целевая структура

```
MarkdownLister/
├── src/
│   ├── MarkdownLister.WLX/    # точка входа WLX, экспорт функций
│   ├── MarkdownLister.Core/   # парсинг, настройки, рендеринг
│   ├── MarkdownLister.Web/    # HTML/CSS/JS-шаблон, ресурсы Prism, Mermaid, KaTeX
│   └── MarkdownLister.Setup/  # установщик / скрипт регистрации
├── themes/                    # JSON-пресеты тем
├── build.ps1
├── README.md
└── LICENSE
```

## Сборка

- `build.ps1` (PowerShell 5.1+, MSBuild VS 2022/BuildTools): restore → сборка x86 и x64 → fail-fast проверка unmanaged-экспорта (11 WLX-функций) → `dist\x86\MarkdownLister.wlx` и `dist\x64\MarkdownLister.wlx64`.
- `MarkdownLister.sln` — для IDE (Debug/Release × Any CPU/x86/x64); платформенные артефакты даёт именно `build.ps1`.
- Обе платформы пишут в один `bin\...\net48`, поэтому dist копируется сразу после каждого прохода; `Rebuild` обязателен — смена `PlatformTarget` не меняет таймстампов входов.
- Тесты и линтеры ТЗ не предусматривает.

## DllExport (3F, v1.8.1) — особенности сборки

Unmanaged-экспорт WLX-функций делает DllExport (ILAsm-проходка). Два известных бага обходятся обёрткой `tools\ildasmwrap\Program.cs` (сборка `tools\iltools\ildasm.exe`, генерируется таргетом `MarkdownListerPrepareIlTools`):

1. **0 экспортов при «зелёной» сборке**: ildasm из форка 3F/coreclr закрывает тела P/Invoke-методов голой `}` без `// end of method ...`; парсер DllExport (MethodParserAction) на таком теле «залипает» и не доходит до экспортных типов. Обёртка дописывает к пустым телам `// end of method patched`.
2. **«Зависание» ilasm / syntax error**: ildasm может выдать BOM UTF-16 при теле UTF-8; ilasm читает файл по BOM и падает с mojibake. Обёртка всегда переписывает выходной IL в UTF-8 с BOM.

Из-за бага №1 P/Invoke-методы нельзя объявлять в экспортируемых классах до экспортных: любая перестановка требует проверки экспорта (`build.ps1` делает её автоматически). Атрибут `[DllExport]` берётся из обфусцированного namespace (см. `DllExportNamespace` в `MarkdownLister.WLX.csproj`) — не сокращать.

## Метаданные автора (обязательно)

Автор: Старинский Олег Григорьевич; email: info@electroman.by; телеграм: @ElectroManBY.

Эти данные обязаны присутствовать в: диалоге «About / О плагине», `README.md`, метаданных сборки (`Company`, `Product`, `Copyright`, `Description` в csproj), комментариях к ключевым модулям. При создании этих файлов/точек — не упускать.

## Особенности реализации WLX

- Экспорт стандартных функций WLX: `ListLoad`, `ListLoadNext`, `ListCloseWindow`, `ListGetDetectString` и др.; корректная обработка `WM_NOTIFY` / `WM_COMMAND` от Total Commander.
- Работа по `F3` (Lister) и в Quick View (`Ctrl+Q`).
- Ошибки не должны «ронять» Lister: показывать сообщение внутри панели просмотра.
- XML-doc-комментарии для публичных методов; разделение UI, логики рендеринга и настроек.

## Версионирование

SemVer (`MAJOR.MINOR.PATCH`, ровно три компонента): номер — в одном месте, `Directory.Build.props`; в коде и скриптах упаковки не хардкодится. Полные правила и сверка перед тегом — скилл `semver`.
