# tc-markdown-lister

Markdown-просмотрщик для Total Commander (WLX-плагин): рендеринг в стиле GitHub, split view, подсветка кода, диаграммы Mermaid, формулы KaTeX, темы, поиск, оглавление, экспорт в PDF.

Работает по `F3` (Lister) и в Quick View (`Ctrl+Q`) для файлов `.md`, `.markdown`, `.mkd`, `.mdown`, `.mdx`.

## Возможности

- Рендеринг Markdown в стиле GitHub: GFM-таблицы, списки задач, сноски, YAML front matter.
- Split view (`Ctrl+M`) — исходник рядом с рендерингом, с синхронизацией прокрутки.
- Подсветка кода (highlight.js): GitHub Light/Dark, Solarized Light/Dark, Monokai.
- Диаграммы Mermaid.js и формулы KaTeX.
- Оглавление (TOC) в боковой панели с навигацией.
- Поиск по документу, масштабирование (50–200 %), ширина колонки чтения (600–2000 px).
- Темы оформления: System / Light / Dark + JSON-пресеты в папке `themes/`.
- Экспорт в PDF, печать, автономный HTML.
- Корректное определение кодировки (UTF-8, UTF-16, ANSI) через UTF.Unknown.
- Защита от «тяжёлых» файлов: предупреждение по размеру, логирование ошибок.
- Локально и без телеметрии: настройки в INI-файле рядом с плагином, никаких сетевых запросов.

## Требования

- Windows; Total Commander (32- или 64-битный).
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (обычно уже установлен вместе с Edge).

## Сборка

```powershell
.\build.ps1                # Release x86 + x64, проверка экспорта, dist\
.\build.ps1 -NoDist        # только собрать и проверить
.\build.ps1 -Configuration Debug
```

Скрипт требует Visual Studio 2022 (или BuildTools) с MSBuild. Результат:

```
dist\x86\MarkdownLister.wlx      # для 32-битного Total Commander
dist\x64\MarkdownLister.wlx64    # для 64-битного Total Commander
```

Каждая сборка проверяется на полный unmanaged-экспорт WLX-функций (11 шт.) — без них Lister не увидит плагин.

Проект также открывается в Visual Studio: `MarkdownLister.sln` (конфигурации Debug/Release × Any CPU/x86/x64).

## Установка

### Вариант 1. Установочный скрипт

```powershell
# из каталога src\MarkdownLister.Setup после сборки (dist\ обязателен)
.\install.ps1                                 # Total Commander найдётся автоматически
.\install.ps1 -TotalCommanderDir "C:\totalcmd" # или укажите путь явно
```

Скрипт скопирует плагин в `<TC>\Plugins\wlx\MarkdownLister\` и зарегистрирует его в `wincmd.ini`. Перезапустите Total Commander. Удаление — `uninstall.ps1`.

### Вариант 2. Вручную

1. Скопируйте `dist\x86\*` и `dist\x64\MarkdownLister.wlx64` в одну папку `<TC>\Plugins\wlx\MarkdownLister\` (имена: `MarkdownLister.wlx` + `MarkdownLister.wlx64` рядом).
2. В `wincmd.ini` добавьте в секцию `[ListerPlugins]` (номер `N` — следующий свободный):

```ini
[N]=%COMMANDER_PATH%\Plugins\wlx\MarkdownLister\MarkdownLister.wlx
[N]_detect="EXT="MD" | EXT="MARKDOWN" | EXT="MKD" | EXT="MDOWN" | EXT="MDX""
```

3. Перезапустите Total Commander.

### Вариант 3. Через pluginst.inf

`src\MarkdownLister.Setup\pluginst.inf` — для установки из zip-архива двойным кликом в Total Commander (штатный механизм TC-плагинов).

## Настройки

INI-файл `MarkdownLister.ini` создаётся рядом с плагином:

```ini
[View]
ThemePreset=        ; имя пресета из themes\ (без .json), пусто — без пресета
Theme=System        ; System | Light | Dark
CodeTheme=github-dark
Zoom=100            ; 50..200
ReadingWidth=900    ; 600..2000
ShowLineNumbers=1
ShowToc=1
SplitView=0
[General]
LogErrors=1
LargeFileWarningMb=5
```

### JSON-пресеты тем

Папка `themes/` содержит редактируемые пресеты (`system.json`, `light.json`, `dark.json`, `solarized-light.json`, `solarized-dark.json`, `monokai.json`):

```json
{
  "name": "Dark",
  "theme": "Dark",
  "codeTheme": "github-dark",
  "zoom": 100,
  "readingWidth": 900,
  "showLineNumbers": true,
  "showToc": true
}
```

Выбор пресета — `ThemePreset=<имя-файла-без-.json>` в INI. Порядок применения: значения по умолчанию → пресет → INI (записи INI персональнее и побеждают). Повреждённый JSON молча игнорируется.

## Структура проекта

```
src/
├── MarkdownLister.WLX/    # точка входа WLX, экспорт функций, WebView2-хост
├── MarkdownLister.Core/   # парсинг Markdown, настройки, статистика
├── MarkdownLister.Web/    # HTML/CSS/JS-шаблон, highlight.js, Mermaid, KaTeX, DOMPurify
└── MarkdownLister.Setup/  # install.ps1, uninstall.ps1, pluginst.inf
themes/                    # JSON-пресеты тем
build.ps1                  # сборка x86/x64 + проверка экспорта + dist
```

## Автор

Старинский Олег Григорьевич

- Email: info@electroman.by
- Telegram: [@ElectroManBY](https://t.me/ElectroManBY)

## Лицензия

[MIT](LICENSE)
