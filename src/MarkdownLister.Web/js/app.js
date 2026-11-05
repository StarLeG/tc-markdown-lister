/* Автор: Старинский Олег Григорьевич (info@electroman.by, @ElectroManBY)
   MarkdownLister: интерфейс просмотра — TOC, поиск, split view, темы, зум.
   Мост к C#-хосту: window.chrome.webview.postMessage / addEventListener('message'). */

(function () {
  'use strict';

  // ---------- Состояние ----------
  const state = {
    html: '',            // отрендеренный HTML от C#-хоста
    source: '',          // исходный Markdown
    headings: [],        // [{level, text, id}]
    settings: {},        // настройки от C#-хоста
    searchHits: [],
    searchIndex: -1,
    scrollSyncLock: false,
    menuOpen: false,     // контекстное меню хоста (WinForms) сейчас показано
    lang: null,          // язык интерфейса: 'ru' | 'en' (из настроек хоста)
    stats: null,         // последние значения статус-строки (для смены языка)
    large: false,        // большой документ: рендеринг чанками, без тяжёлых украшений
    imageBase: '',       // базовый URL папки документа (виртуальный хост)
    imageRootBase: '',   // базовый URL корня раздачи (диск/шара документа)
    imageRoot: '',       // корень раздачи в виде пути (C:\, \\server\share\)
    sourceLoaded: false, // исходник (split view) уже получен
    sourceRequested: false, // запрос исходника уже отправлен хосту
    update: null,        // состояние проверки обновлений ({status, version, url, manual})
  };

  const $ = (sel) => document.querySelector(sel);
  const content = $('#content');
  const contentScroll = $('#content-scroll');
  const sourceScroll = $('#source-scroll');
  const sourceText = $('#source-text');

  // ---------- Локализация (ru / en) ----------
  // Строки хоста (меню, диалоги) локализуются в C# (Loc.cs); здесь — строки страницы.
  const I18N = {
    ru: {
      sidebarTitle: 'Оглавление',
      hideToc: 'Скрыть оглавление (Ctrl+O)',
      searchPlaceholder: 'Поиск по документу…',
      searchPrev: 'Предыдущее (Shift+Enter)',
      searchNext: 'Следующее (Enter)',
      searchClose: 'Закрыть (Esc)',
      scrollTop: 'Наверх',
      words: ['слово', 'слова', 'слов'],
      headings: ['заголовок', 'заголовка', 'заголовков'],
      min: 'мин',
      copy: 'Копировать',
      copied: 'Скопировано!',
      mermaidNotLoaded: 'Диаграмма Mermaid не отрисована: библиотека mermaid не загрузилась.',
      mermaidError: 'Ошибка диаграммы Mermaid: ',
      errorTitle: 'Ошибка',
      unknownError: 'Неизвестная ошибка',
      scriptLoadError: 'Не удалось загрузить ',
      loading: 'Загрузка документа…',
      largeBanner: 'Большой файл ({n} МБ): подсветка кода, диаграммы, формулы и номера строк отключены для производительности.',
      hideBanner: 'Скрыть',
      updateChecking: 'Проверка обновлений…',
      updateLatest: 'Установлена последняя версия',
      updateAvailable: 'Доступна новая версия {v} ↗',
      updateError: 'Не удалось проверить обновления',
      updateOpen: 'Открыть страницу релиза',
    },
    en: {
      sidebarTitle: 'Contents',
      hideToc: 'Hide contents (Ctrl+O)',
      searchPlaceholder: 'Find in document…',
      searchPrev: 'Previous (Shift+Enter)',
      searchNext: 'Next (Enter)',
      searchClose: 'Close (Esc)',
      scrollTop: 'Back to top',
      words: ['word', 'words'],
      headings: ['heading', 'headings'],
      min: 'min',
      copy: 'Copy',
      copied: 'Copied!',
      mermaidNotLoaded: 'Mermaid diagram not rendered: the mermaid library failed to load.',
      mermaidError: 'Mermaid diagram error: ',
      errorTitle: 'Error',
      unknownError: 'Unknown error',
      scriptLoadError: 'Failed to load ',
      loading: 'Loading document…',
      largeBanner: 'Large file ({n} MB): syntax highlighting, diagrams, formulas and line numbers are disabled for performance.',
      hideBanner: 'Hide',
      updateChecking: 'Checking for updates…',
      updateLatest: 'You have the latest version',
      updateAvailable: 'New version {v} available ↗',
      updateError: 'Could not check for updates',
      updateOpen: 'Open the release page',
    },
  };

  function T(key) {
    return (I18N[state.lang] || I18N.ru)[key];
  }

  // ---------- Мост к C#-хосту ----------
  function post(msg) {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage(JSON.stringify(msg));
    }
  }

  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener('message', (e) => {
      let msg = e.data;
      if (typeof msg === 'string') {
        try { msg = JSON.parse(msg); } catch { return; }
      }
      handleHostMessage(msg);
    });
  }

  function handleHostMessage(msg) {
    switch (msg.type) {
      case 'loading':
        setLoading(null);
        break;
      case 'load':
        renderDocument(msg.html, msg.source, msg.headings, msg.settings, msg.stats,
          msg.imageBase, msg.imageRoot, msg.imageRootBase);
        break;
      case 'load-begin':
        beginLargeDocument(msg);
        break;
      case 'load-chunk':
        appendChunk(msg);
        break;
      case 'load-end':
        endLargeDocument();
        break;
      case 'source-chunk':
        appendSourceChunk(msg.text);
        break;
      case 'source-end':
        state.sourceLoaded = true;
        break;
      case 'settings':
        applySettings(msg.settings);
        break;
      case 'find':
        openSearch(msg.text || '');
        break;
      case 'find-next':
        gotoHit(1);
        break;
      case 'find-prev':
        gotoHit(-1);
        break;
      case 'scroll-percent':
        scrollToPercent(msg.value);
        break;
      case 'set-theme':
        setTheme(msg.value);
        break;
      case 'set-zoom':
        setZoom(msg.value);
        break;
      case 'toggle-split':
        toggleSplitView();
        break;
      case 'toggle-toc':
        toggleSidebar();
        break;
      case 'error':
        showError(msg.text);
        break;
      case 'update-state':
        applyUpdateState(msg);
        break;
      case 'menu-opened':
        state.menuOpen = true;
        break;
      case 'menu-closed':
        state.menuOpen = false;
        break;
    }
  }

  // ---------- Проверка обновлений ----------
  // Статус в статус-строке: найденная версия (клик — открыть страницу релиза)
  // либо результат ручной проверки (клик — скрыть). Автопроверка без обновления
  // ничего не показывает — без лишнего шума.
  function applyUpdateState(msg) {
    const el = $('#status-update');
    state.update = {
      status: msg.status, version: msg.version || '', url: msg.url || '', manual: !!msg.manual,
    };
    el.classList.remove('update-muted');

    if (msg.status === 'available') {
      el.textContent = T('updateAvailable').replace('{v}', msg.version || '');
      el.title = T('updateOpen');
    } else if (msg.status === 'checking') {
      el.textContent = T('updateChecking');
      el.classList.add('update-muted');
      el.title = '';
    } else if (msg.manual && msg.status === 'latest') {
      el.textContent = T('updateLatest');
      el.classList.add('update-muted');
      el.title = '';
    } else if (msg.manual && msg.status === 'error') {
      el.textContent = T('updateError');
      el.classList.add('update-muted');
      el.title = '';
    } else {
      el.hidden = true;
      el.textContent = '';
      return;
    }
    el.hidden = false;
  }

  // ---------- Рендеринг документа ----------
  function renderDocument(html, source, headings, settings, stats, imageBase, imageRoot, imageRootBase) {
    state.html = html || '';
    state.source = source || '';
    state.headings = headings || [];
    state.settings = settings || {};
    state.large = false;
    state.imageBase = imageBase || '';
    state.imageRoot = imageRoot || '';
    state.imageRootBase = imageRootBase || '';
    state.sourceLoaded = !!source;
    state.sourceRequested = false;

    content.innerHTML = '';
    appendHtmlFragment(state.html);

    sourceText.textContent = state.source;
    applySettings(state.settings);
    buildToc();
    enhanceCodeBlocks();
    renderMath();
    renderMermaid();
    updateStats(stats);
    contentScroll.scrollTop = 0;
    clearSearch();
    hideLoading();
    hideLargeBanner();
  }

  // ---------- Большой документ: постепенный рендеринг чанками ----------
  function beginLargeDocument(msg) {
    state.html = '';
    state.source = '';
    state.headings = msg.headings || [];
    state.settings = msg.settings || {};
    state.large = true;
    state.imageBase = msg.imageBase || '';
    state.imageRoot = msg.imageRoot || '';
    state.imageRootBase = msg.imageRootBase || '';
    state.sourceLoaded = false;
    state.sourceRequested = false;

    content.innerHTML = '';
    sourceText.textContent = '';
    applySettings(state.settings);
    buildToc();
    updateStats(msg.stats);
    contentScroll.scrollTop = 0;
    clearSearch();
    showLargeBanner(msg.size || 0);
    setLoading(0);
  }

  function appendChunk(msg) {
    appendHtmlFragment(msg.html);
    if (typeof msg.percent === 'number') setLoading(msg.percent);
  }

  function endLargeDocument() {
    hideLoading();
    updateTopButton();
  }

  // Добавляет очередной кусок HTML в документ: санитизация (DOMPurify) и
  // переписывание путей картинок выполняются по чанку, а не по всему документу.
  function appendHtmlFragment(html) {
    if (!html) return;
    const tpl = document.createElement('template');
    tpl.innerHTML = sanitizeHtml(html);
    rewriteImageUrlsIn(tpl.content);
    content.appendChild(tpl.content);
  }

  // Санитизация через DOMPurify (защита от встроенного враждебного HTML)
  function sanitizeHtml(html) {
    if (!window.DOMPurify) return html;
    return DOMPurify.sanitize(html, {
      ADD_ATTR: ['target', 'rel', 'class', 'id'],
      ADD_TAGS: ['input'],            // чекбоксы списков задач
      FORBID_TAGS: ['script', 'style', 'iframe', 'object', 'embed', 'form'],
      // Пути Windows-файлов (C:\..., \\server\share\...) в src картинок — это
      // локальные пути, а не «небезопасные схемы»: разрешаем их (переписывание
      // на виртуальный хост происходит после санитизации).
      ALLOWED_URI_REGEXP: /^(?:(?:(?:f|ht)tps?|mailto|tel|callto|sms|cid|xmpp):|[^a-z]|[a-z+.\-]+(?:[^a-z+.\-:]|$)|[a-z]:[\\/]|\\\\)/i,
    });
  }

  // Переписывает пути локальных картинок на виртуальный хост корня документа.
  // Относительные (включая ./ и ../) резолвятся против папки документа;
  // абсолютные пути файловой системы — если лежат на том же корне (диске/шаре),
  // что и документ. Внешние URL, data: и якоря не трогаем.
  function rewriteImageUrlsIn(root) {
    if (!state.imageBase) return;
    root.querySelectorAll('img').forEach((img) => {
      const src = img.getAttribute('src') || '';
      if (!src || /^(https?:|data:|blob:|\/\/|#)/i.test(src)) return;
      const norm = src.replace(/\\/g, '/');
      try {
        if (/^([a-z]:\/|\/\/[^/])/i.test(norm)) {
          // Абсолютный путь (C:/..., //server/share/...) — свой корень отдаём
          // через виртуальный хост, чужой оставляем как есть.
          const rootNorm = (state.imageRoot || '').replace(/\\/g, '/');
          if (rootNorm && norm.toLowerCase().startsWith(rootNorm.toLowerCase())) {
            img.src = new URL(norm.substring(rootNorm.length), state.imageRootBase).href;
          }
          return;
        }
        if (norm.startsWith('/')) return;
        img.src = new URL(norm, state.imageBase).href;
      } catch { /* оставляем src как есть */ }
    });
  }

  // ---------- Индикатор загрузки / предупреждение о большом файле ----------
  function setLoading(percent) {
    const el = $('#loading');
    el.hidden = false;
    el.textContent = (typeof percent === 'number')
      ? T('loading') + ' ' + percent + '%'
      : T('loading');
  }

  function hideLoading() {
    $('#loading').hidden = true;
  }

  function showLargeBanner(sizeBytes) {
    const banner = $('#large-banner');
    const mb = Math.max(1, Math.round(sizeBytes / 1048576));
    $('#large-banner-text').textContent = T('largeBanner').replace('{n}', mb);
    $('#large-banner-close').title = T('hideBanner');
    banner.hidden = false;
  }

  function hideLargeBanner() {
    $('#large-banner').hidden = true;
  }

  // ---------- Ленивый исходник для split view (большие документы) ----------
  // Полный исходник в DOM не дублируется: хост стримит его чанками по запросу,
  // только когда пользователь включил split view.
  function ensureSource() {
    if (!state.large || state.sourceLoaded || state.sourceRequested) return;
    state.sourceRequested = true;
    post({ type: 'need-source' });
  }

  function appendSourceChunk(text) {
    if (text) sourceText.appendChild(document.createTextNode(text));
  }

  // ---------- Настройки / темы / зум ----------
  function applySettings(s) {
    if (!s) return;
    state.settings = Object.assign({}, state.settings, s);
    if (s.language) applyLang(s.language);
    setTheme(s.theme || 'System');
    setZoom(s.zoom || 100);
    document.documentElement.style.setProperty('--reading-width', (s.readingWidth || 900) + 'px');
    $('#sidebar').hidden = !s.showToc;
    sourceScroll.hidden = !s.splitView;
    document.body.classList.toggle('no-line-numbers', s.showLineNumbers === false);
    if (s.codeTheme) setCodeTheme(s.codeTheme);
    if (s.splitView) ensureSource();
  }

  // Применяет язык интерфейса: статические подписи, кнопки копирования, статус-строка.
  // Значения берутся из I18N; хост присылает эффективный язык в настройках (language).
  function applyLang(lang) {
    const l = (lang === 'en') ? 'en' : 'ru';
    if (state.lang === l) return;
    state.lang = l;
    document.documentElement.lang = l;

    $('#sidebar-title').textContent = T('sidebarTitle');
    $('#btn-close-sidebar').title = T('hideToc');
    $('#search-input').placeholder = T('searchPlaceholder');
    $('#search-prev').title = T('searchPrev');
    $('#search-next').title = T('searchNext');
    $('#search-close').title = T('searchClose');
    $('#btn-top').title = T('scrollTop');

    content.querySelectorAll('.copy-btn').forEach((b) => { b.textContent = T('copy'); });
    updateStats(state.stats);
    if (state.update) applyUpdateState(state.update);
  }

  function setTheme(theme) {
    // Класс темы — на <html>: body должен видеть CSS-переменные (--fg, --bg и др.)
    const root = document.documentElement;
    root.classList.remove('theme-light', 'theme-dark');
    if (theme === 'Light') {
      root.classList.add('theme-light');
    } else if (theme === 'Dark') {
      root.classList.add('theme-dark');
    } else {
      // System: по предпочтению ОС
      const prefersDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
      root.classList.add(prefersDark ? 'theme-dark' : 'theme-light');
    }
    state.settings.theme = theme;
  }

  function setCodeTheme(name) {
    const link = $('#code-theme');
    const safe = String(name).replace(/[^a-z0-9-]/gi, '');
    link.href = 'vendor/highlight/styles/' + safe + '.min.css';
    state.settings.codeTheme = name;
  }

  function setZoom(percent) {
    const z = Math.min(200, Math.max(50, Number(percent) || 100)) / 100;
    document.documentElement.style.setProperty('--zoom', z);
    state.settings.zoom = Math.round(z * 100);
    post({ type: 'settings-changed', key: 'zoom', value: state.settings.zoom });
  }

  // ---------- Оглавление (TOC) ----------
  function buildToc() {
    const toc = $('#toc');
    toc.innerHTML = '';

    // Если C# не прислал заголовки — извлекаем из DOM
    let headings = state.headings;
    if (!headings.length) {
      headings = Array.from(content.querySelectorAll('h1,h2,h3,h4,h5,h6')).map((h) => ({
        level: Number(h.tagName.substring(1)),
        text: h.textContent,
        id: h.id || '',
      }));
    }

    headings.forEach((h) => {
      const a = document.createElement('a');
      a.textContent = h.text;
      a.dataset.level = h.level;
      if (h.id) a.dataset.target = h.id;
      a.href = '#' + (h.id || '');
      a.addEventListener('click', (e) => {
        e.preventDefault();
        scrollToHeading(h.id);
      });
      toc.appendChild(a);
    });

    observeHeadings();
  }

  function scrollToHeading(id) {
    if (!id) return;
    // href в Markdown кодируется (например, кириллица в якорях) — раскодируем
    try { id = decodeURIComponent(id); } catch (e) { /* оставляем как есть */ }
    let el = document.getElementById(id);
    if (!el) {
      // Якорь, возможно, не пережил санитизацию — ищем по тексту заголовка
      const h = state.headings.find((x) => x.id === id);
      if (h) {
        el = Array.from(content.querySelectorAll('h1,h2,h3,h4,h5,h6'))
          .find((n) => n.textContent.trim() === h.text);
      }
    }
    if (el) {
      el.scrollIntoView({ behavior: 'smooth', block: 'start' });
      post({ type: 'navigated', id: id });
    }
  }

  let headingObserver = null;
  function observeHeadings() {
    if (headingObserver) headingObserver.disconnect();
    const links = Array.from($('#toc').querySelectorAll('a[data-target]'));
    if (!links.length || !window.IntersectionObserver) return;

    headingObserver = new IntersectionObserver((entries) => {
      entries.forEach((entry) => {
        if (entry.isIntersecting) {
          links.forEach((l) => l.classList.remove('active'));
          const active = links.find((l) => l.dataset.target === entry.target.id);
          if (active) active.classList.add('active');
        }
      });
    }, { root: contentScroll, rootMargin: '0px 0px -75% 0px', threshold: 0 });

    content.querySelectorAll('h1,h2,h3,h4,h5,h6').forEach((h) => {
      if (h.id) headingObserver.observe(h);
    });
  }

  function toggleSidebar() {
    const sb = $('#sidebar');
    sb.hidden = !sb.hidden;
    post({ type: 'settings-changed', key: 'showToc', value: !sb.hidden });
  }

  // ---------- Поиск ----------
  function openSearch(initial) {
    const bar = $('#search-bar');
    bar.hidden = false;
    const input = $('#search-input');
    if (initial) input.value = initial;
    input.focus();
    input.select();
    runSearch();
  }

  function closeSearch() {
    $('#search-bar').hidden = true;
    clearSearch();
  }

  function clearSearch() {
    // Снимаем старую подсветку
    content.querySelectorAll('mark.search-hit').forEach((m) => {
      const parent = m.parentNode;
      while (m.firstChild) parent.insertBefore(m.firstChild, m);
      parent.removeChild(m);
      parent.normalize();
    });
    state.searchHits = [];
    state.searchIndex = -1;
    updateSearchCount();
  }

  // Служебный текст, который не является содержимым документа: кнопки
  // «Копировать», ряды номеров строк, скрытая MathML-копия формул KaTeX.
  const SEARCH_SKIP = 'button, .line-numbers-rows, .katex-mathml, a.anchor';
  // Блочные контейнеры: совпадение не пересекает их границу (абзац, ячейку,
  // заголовок), но проходит через инлайн-разметку — токены подсветки кода,
  // <code>, <strong> и т. п. Именно это нужно при поиске внутри блоков кода.
  const SEARCH_BLOCK = 'p, pre, li, h1, h2, h3, h4, h5, h6, td, th, dt, dd, blockquote, figcaption, div, section, article';

  // Раскладывает текст содержимого по блокам: блок -> [{node|null, start, end}].
  // node === null — фантомный перевод строки (<br>); через него совпадение не
  // проходит, т. к. строка запроса не содержит '\n'.
  function collectSearchSegments() {
    const blocks = [];
    const stack = [{ segs: [], len: 0 }];
    blocks.push(stack[0]);
    const push = (node, text) => {
      const cur = stack[stack.length - 1];
      cur.segs.push({ node: node, start: cur.len, end: cur.len + text.length });
      cur.len += text.length;
    };
    (function walk(el) {
      for (let child = el.firstChild; child; child = child.nextSibling) {
        if (child.nodeType === Node.TEXT_NODE) {
          if (child.nodeValue) push(child, child.nodeValue);
        } else if (child.nodeType === Node.ELEMENT_NODE) {
          const tag = child.tagName;
          if (tag === 'SCRIPT' || tag === 'STYLE' || child.matches(SEARCH_SKIP)) continue;
          if (tag === 'BR') { push(null, '\n'); continue; }
          const nested = child.matches(SEARCH_BLOCK);
          if (nested) {
            const block = { segs: [], len: 0 };
            blocks.push(block);
            stack.push(block);
          }
          walk(child);
          if (nested) stack.pop();
        }
      }
    })(content);
    return blocks;
  }

  function runSearch() {
    clearSearch();
    const q = $('#search-input').value;
    if (!q) return;

    const needle = q.toLowerCase();
    const hits = []; // каждый hit — массив mark-элементов одного совпадения

    collectSearchSegments().forEach((block) => {
      if (!block.len) return;
      let concat = '';
      block.segs.forEach((s) => { concat += s.node ? s.node.nodeValue : '\n'; });
      const lower = concat.toLowerCase();

      const matches = [];
      let pos = 0;
      let idx;
      while ((idx = lower.indexOf(needle, pos)) !== -1) {
        matches.push([idx, idx + needle.length]);
        pos = idx + needle.length;
      }
      if (!matches.length) return;

      // Диапазоны совпадений распределяем по текстовым узлам блока
      const perNode = new Map();
      matches.forEach((m, mi) => {
        block.segs.forEach((s) => {
          if (!s.node) return;
          const from = Math.max(m[0], s.start);
          const to = Math.min(m[1], s.end);
          if (from < to) {
            let arr = perNode.get(s.node);
            if (!arr) { arr = []; perNode.set(s.node, arr); }
            arr.push({ start: from - s.start, end: to - s.start, mi: mi });
          }
        });
      });

      // Каждый узел переписывается один раз — сразу со всеми своими кусками
      const blockHits = matches.map(() => []);
      perNode.forEach((ranges, node) => {
        ranges.sort((a, b) => a.start - b.start);
        const text = node.nodeValue;
        const frag = document.createDocumentFragment();
        let cursor = 0;
        ranges.forEach((r) => {
          if (r.start > cursor) frag.appendChild(document.createTextNode(text.substring(cursor, r.start)));
          const mark = document.createElement('mark');
          mark.className = 'search-hit';
          mark.textContent = text.substring(r.start, r.end);
          frag.appendChild(mark);
          blockHits[r.mi].push(mark);
          cursor = r.end;
        });
        if (cursor < text.length) frag.appendChild(document.createTextNode(text.substring(cursor)));
        node.parentNode.replaceChild(frag, node);
      });

      blockHits.forEach((marks) => { if (marks.length) hits.push(marks); });
    });

    state.searchHits = hits;
    state.searchIndex = state.searchHits.length ? 0 : -1;
    highlightCurrent();
    updateSearchCount();
  }

  function gotoHit(dir) {
    if (!state.searchHits.length) return;
    state.searchIndex = (state.searchIndex + dir + state.searchHits.length) % state.searchHits.length;
    highlightCurrent();
    updateSearchCount();
  }

  function highlightCurrent() {
    // Одно совпадение может быть несколькими mark-элементами (когда текст
    // разбит инлайн-разметкой) — подсвечиваем и проматываем всю группу.
    state.searchHits.forEach((marks, i) => {
      const on = i === state.searchIndex;
      marks.forEach((m) => m.classList.toggle('current', on));
    });
    const cur = state.searchHits[state.searchIndex];
    if (cur && cur[0]) cur[0].scrollIntoView({ behavior: 'smooth', block: 'center' });
  }

  function updateSearchCount() {
    const n = state.searchHits.length;
    const i = n ? state.searchIndex + 1 : 0;
    $('#search-count').textContent = i + ' / ' + n;
  }

  // ---------- Split view и синхронизация прокрутки ----------
  function toggleSplitView() {
    const hidden = sourceScroll.hidden;
    sourceScroll.hidden = !hidden;
    state.settings.splitView = hidden;
    post({ type: 'settings-changed', key: 'splitView', value: hidden });
    if (hidden) {
      ensureSource();
      syncScroll(contentScroll, sourceScroll);
    }
  }

  function syncScroll(from, to) {
    if (state.scrollSyncLock) return;
    state.scrollSyncLock = true;
    const maxFrom = from.scrollHeight - from.clientHeight;
    const maxTo = to.scrollHeight - to.clientHeight;
    const ratio = maxFrom > 0 ? from.scrollTop / maxFrom : 0;
    to.scrollTop = ratio * maxTo;
    requestAnimationFrame(() => { state.scrollSyncLock = false; });
  }

  contentScroll.addEventListener('scroll', () => {
    if (!sourceScroll.hidden) syncScroll(contentScroll, sourceScroll);
    updateProgress();
    updateTopButton();
  });

  sourceScroll.addEventListener('scroll', () => {
    if (!sourceScroll.hidden) syncScroll(sourceScroll, contentScroll);
  });

  // ---------- Статус-строка ----------
  function updateStats(stats) {
    if (!stats) return;
    state.stats = stats;
    $('#status-words').textContent = plural(stats.words, T('words'));
    $('#status-time').textContent = '~' + stats.readingTime + ' ' + T('min');
    $('#status-headings').textContent = plural(stats.headings, T('headings'));
  }

  function plural(n, forms) {
    if (state.lang === 'en')
      return n + ' ' + (Math.abs(n) === 1 ? forms[0] : forms[1]);
    const a = Math.abs(n) % 100;
    const b = a % 10;
    const word = (a > 10 && a < 20) ? forms[2]
      : (b > 1 && b < 5) ? forms[1]
      : (b === 1) ? forms[0]
      : forms[2];
    return n + ' ' + word;
  }

  function updateProgress() {
    const max = contentScroll.scrollHeight - contentScroll.clientHeight;
    const pct = max > 0 ? Math.round((contentScroll.scrollTop / max) * 100) : 0;
    $('#status-progress').textContent = pct + '%';
    post({ type: 'scroll-percent', value: pct });
  }

  function scrollToPercent(pct) {
    const max = contentScroll.scrollHeight - contentScroll.clientHeight;
    contentScroll.scrollTop = (Math.min(100, Math.max(0, pct)) / 100) * max;
  }

  function updateTopButton() {
    $('#btn-top').classList.toggle('visible', contentScroll.scrollTop > 300);
  }

  // ---------- Блоки кода: подсветка, номера строк, копирование ----------
  function enhanceCodeBlocks() {
    content.querySelectorAll('pre > code').forEach((code) => {
      const pre = code.parentElement;

      // Определяем язык из class="language-xxx"
      const langMatch = /language-(\S+)/.exec(code.className || '');
      const lang = langMatch ? langMatch[1] : '';

      // Диаграммы Mermaid — отдельная обработка, без подсветки
      if (lang === 'mermaid') {
        code.classList.add('mermaid-src');
        return;
      }

      if (window.hljs && lang) {
        try { hljs.highlightElement(code); } catch { /* неизвестный язык — оставляем как есть */ }
      }

      // Обёртка для кнопки копирования
      pre.classList.add('code-block');
      const btn = document.createElement('button');
      btn.className = 'copy-btn';
      btn.textContent = T('copy');
      btn.addEventListener('click', () => {
        const text = code.textContent;
        if (navigator.clipboard) {
          navigator.clipboard.writeText(text).then(() => {
            btn.textContent = T('copied');
            setTimeout(() => { btn.textContent = T('copy'); }, 1500);
          });
        } else {
          // Фолбэк для старых движков
          const ta = document.createElement('textarea');
          ta.value = text;
          document.body.appendChild(ta);
          ta.select();
          document.execCommand('copy');
          document.body.removeChild(ta);
          btn.textContent = T('copied');
          setTimeout(() => { btn.textContent = T('copy'); }, 1500);
        }
      });
      pre.appendChild(btn);

      // Номера строк
      if (state.settings.showLineNumbers !== false) {
        pre.classList.add('line-numbers');
        const rows = document.createElement('span');
        rows.className = 'line-numbers-rows';
        const lineCount = code.textContent.split('\n').length;
        for (let i = 0; i < lineCount; i++) {
          rows.appendChild(document.createElement('span'));
        }
        pre.insertBefore(rows, code);
      }
    });
  }

  // ---------- Ленивая загрузка библиотек ----------
  // KaTeX (~275 КБ) и Mermaid (~3 МБ) не грузятся при старте страницы:
  // подключаются только если в документе есть формулы или диаграммы.
  const loadedScripts = {};
  function loadScript(src) {
    if (loadedScripts[src]) return loadedScripts[src];
    loadedScripts[src] = new Promise((resolve, reject) => {
      const s = document.createElement('script');
      s.src = src;
      s.onload = () => resolve();
      s.onerror = () => reject(new Error(T('scriptLoadError') + src));
      document.body.appendChild(s);
    });
    return loadedScripts[src];
  }

  // Есть ли в исходнике математические конструкции ($…$, $$…$$, \(…\), \[…\])
  function hasMath(text) {
    return /\$\$|\\\(|\\\[/.test(text) || /\$[^$\n$]+\$/m.test(text);
  }

  // ---------- KaTeX ----------
  function renderMath() {
    if (!hasMath(state.source)) return;   // формул нет — библиотека не нужна
    if (window.renderMathInElement) {
      applyMath();
      return;
    }
    // KaTeX: katex.min.js, затем auto-render.min.js (зависит от katex)
    loadScript('vendor/katex/katex.min.js')
      .then(() => loadScript('vendor/katex/contrib/auto-render.min.js'))
      .then(applyMath)
      .catch((e) => { console.error('KaTeX:', e); });

    function applyMath() {
      if (!window.renderMathInElement) return;
      try {
        renderMathInElement(content, {
          delimiters: [
            { left: '$$', right: '$$', display: true },
            { left: '\\[', right: '\\]', display: true },
            { left: '$', right: '$', display: false },
            { left: '\\(', right: '\\)', display: false },
          ],
          throwOnError: false,
        });
      } catch { /* ошибка формулы не должна ломать просмотр */ }
    }
  }

  // ---------- Mermaid ----------
  function renderMermaid() {
    // Markdig (UseAdvancedExtensions -> Diagrams) оборачивает ```mermaid в <div class="mermaid">;
    // поддерживаем и классический вариант <pre><code class="language-mermaid"> (mermaid-src).
    content.querySelectorAll('code.mermaid-src, code.language-mermaid').forEach((code) => {
      const pre = code.parentElement;
      const div = document.createElement('div');
      div.className = 'mermaid';
      div.textContent = code.textContent;
      pre.replaceWith(div);
    });

    const blocks = content.querySelectorAll('.mermaid');
    if (!blocks.length) return;   // диаграмм нет — mermaid.min.js (~3 МБ) не грузим

    if (!window.mermaid) {
      // Ленивая загрузка: mermaid тяжёлый, грузим только при наличии диаграмм
      loadScript('vendor/mermaid/mermaid.min.js')
        .then(() => renderMermaidBlocks(blocks))
        .catch((e) => {
          console.error('Mermaid:', e);
          blocks.forEach((b) => {
            b.textContent = T('mermaidNotLoaded');
          });
        });
      return;
    }
    renderMermaidBlocks(blocks);
  }

  function renderMermaidBlocks(blocks) {
    if (!window.mermaid) return;

    mermaid.initialize({
      startOnLoad: false,
      theme: document.documentElement.classList.contains('theme-dark') ? 'dark' : 'default',
      securityLevel: 'strict',
    });

    try {
      const runResult = mermaid.run({ nodes: blocks });
      if (runResult && typeof runResult.catch === 'function') {
        runResult.catch((e) => {
          console.error('Mermaid:', e);
          blocks.forEach((b) => {
            if (!b.querySelector('svg')) {
              b.textContent = T('mermaidError') + (e && e.message ? e.message : String(e));
            }
          });
        });
      }
    } catch (e) {
      console.error('Mermaid:', e);
    }
  }

  // ---------- Ошибка ----------
  function showError(text) {
    hideLoading();
    hideLargeBanner();
    content.innerHTML = '<div style="padding:2em;text-align:center;color:#c0392b;">' +
      '<h2>' + T('errorTitle') + '</h2><p></p></div>';
    content.querySelector('p').textContent = text || T('unknownError');
  }

  // ---------- Клавиатура ----------
  document.addEventListener('keydown', (e) => {
    const ctrl = e.ctrlKey || e.metaKey;

    if (ctrl && e.key === 'f') {
      e.preventDefault();
      openSearch('');
    } else if (ctrl && e.key === 'm') {
      e.preventDefault();
      toggleSplitView();
    } else if (ctrl && e.key === 'o') {
      e.preventDefault();
      toggleSidebar();
    } else if (ctrl && (e.key === '=' || e.key === '+')) {
      e.preventDefault();
      setZoom((state.settings.zoom || 100) + 10);
    } else if (ctrl && e.key === '-') {
      e.preventDefault();
      setZoom((state.settings.zoom || 100) - 10);
    } else if (ctrl && e.key === '0') {
      e.preventDefault();
      setZoom(100);
    } else if (ctrl && e.key === 'p') {
      e.preventDefault();
      post({ type: 'print' });
    } else if (e.key === 'ContextMenu' || (e.shiftKey && e.key === 'F10')) {
      // Клавиша контекстного меню / Shift+F10 — показываем меню плагина
      e.preventDefault();
      post({ type: 'context-menu' });
    } else if (e.key === 'F3') {
      e.preventDefault();
      gotoHit(e.shiftKey ? -1 : 1);
    } else if (e.key === 'Escape') {
      if (!$('#search-bar').hidden) {
        closeSearch();
      } else {
        // Ничего не открыто — Escape закрывает окно просмотра, как в самом TC.
        // (Открытое контекстное меню перехватывает Escape раньше — см. обработчик меню.)
        e.preventDefault();
        post({ type: 'close' });
      }
    } else if (e.key === 'Enter' && !$('#search-bar').hidden && document.activeElement === $('#search-input')) {
      e.preventDefault();
      gotoHit(e.shiftKey ? -1 : 1);
    }
  });

  $('#search-input').addEventListener('input', debounce(runSearch, 220));
  $('#search-next').addEventListener('click', () => gotoHit(1));
  $('#search-prev').addEventListener('click', () => gotoHit(-1));
  $('#search-close').addEventListener('click', closeSearch);
  $('#btn-top').addEventListener('click', () => contentScroll.scrollTo({ top: 0, behavior: 'smooth' }));
  $('#btn-close-sidebar').addEventListener('click', toggleSidebar);
  $('#large-banner-close').addEventListener('click', hideLargeBanner);
  $('#status-update').addEventListener('click', () => {
    const u = state.update;
    if (!u) return;
    if (u.status === 'available' && u.url) {
      post({ type: 'open-external', href: u.url });
    } else if (u.status === 'latest' || u.status === 'error') {
      // Результат ручной проверки — гасим по клику
      $('#status-update').hidden = true;
    }
  });

  // Внутренние ссылки документа (other.md#section) — передаём хосту
  content.addEventListener('click', (e) => {
    const a = e.target.closest('a');
    if (!a) return;
    const href = a.getAttribute('href') || '';

    if (href.startsWith('#')) {
      e.preventDefault();
      scrollToHeading(href.substring(1));
      return;
    }
    // Относительные ссылки на .md — переход к другому документу через хост
    if (/^[^:]+\.md(#.*)?$/i.test(href)) {
      e.preventDefault();
      post({ type: 'navigate', href: href });
      return;
    }
    // Внешние ссылки — открывать через хост (ShellExecute)
    if (/^https?:/i.test(href)) {
      e.preventDefault();
      post({ type: 'open-external', href: href });
    }
  });

  // Контекстное меню плагина по правому клику.
  // Событие contextmenu в странице надёжнее, чем ContextMenuRequested в C#-хосте.
  document.addEventListener('contextmenu', (e) => {
    e.preventDefault();
    post({ type: 'context-menu' });
  });

  // Закрытие контекстного меню хоста: клики внутри WebView2 не гасят
  // WinForms-меню сами (браузер — дочернее окно, активация не переходит),
  // поэтому закрываем его явно через menu-dismiss. Первый клик «съедается» —
  // как это делают нативные меню, чтобы случайно не перейти по ссылке.
  // Клик гасим по таймауту: menu-closed может прийти раньше самого click.
  let menuSwallowUntil = 0;

  document.addEventListener('pointerdown', (e) => {
    if (!state.menuOpen) return;
    post({ type: 'menu-dismiss' });
    if (e.button === 0 || e.button === 1) {
      e.preventDefault();
      e.stopPropagation();
      menuSwallowUntil = Date.now() + 300;
    }
  }, true);

  document.addEventListener('click', (e) => {
    if (Date.now() >= menuSwallowUntil) return;
    menuSwallowUntil = 0;
    e.preventDefault();
    e.stopPropagation();
  }, true);

  document.addEventListener('keydown', (e) => {
    if (state.menuOpen && e.key === 'Escape') {
      e.preventDefault();
      e.stopPropagation();
      post({ type: 'menu-dismiss' });
    }
  }, true);

  document.addEventListener('wheel', () => {
    if (state.menuOpen) post({ type: 'menu-dismiss' });
  }, { capture: true, passive: true });

  // Уведомляем хост о готовности
  post({ type: 'ready' });

  // ---------- Утилиты ----------
  function debounce(fn, ms) {
    let t;
    return function (...args) {
      clearTimeout(t);
      t = setTimeout(() => fn.apply(this, args), ms);
    };
  }
})();
