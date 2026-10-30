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

- MSBuild (Visual Studio 2022) или `build.ps1`; выход — `.wlx` для x86 и x64 с регистрацией в Total Commander. Скриптов сборки ещё нет — это целевые команды.
- Тесты и линтеры ТЗ не предусматривает.

## Метаданные автора (обязательно)

Автор: Старинский Олег Григорьевич; email: info@electroman.by; телеграм: @ElectroManBY.

Эти данные обязаны присутствовать в: диалоге «About / О плагине», `README.md`, метаданных сборки (`Company`, `Product`, `Copyright`, `Description` в csproj), комментариях к ключевым модулям. При создании этих файлов/точек — не упускать.

## Особенности реализации WLX

- Экспорт стандартных функций WLX: `ListLoad`, `ListLoadNext`, `ListCloseWindow`, `ListGetDetectString` и др.; корректная обработка `WM_NOTIFY` / `WM_COMMAND` от Total Commander.
- Работа по `F3` (Lister) и в Quick View (`Ctrl+Q`).
- Ошибки не должны «ронять» Lister: показывать сообщение внутри панели просмотра.
- XML-doc-комментарии для публичных методов; разделение UI, логики рендеринга и настроек.

## Версионирование

SemVer (`MAJOR.MINOR.PATCH`, ровно три компонента): номер — в одном месте, `.csproj`; в коде и скриптах упаковки не хардкодится. Полные правила и сверка перед тегом — скилл `semver`.
