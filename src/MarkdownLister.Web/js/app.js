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
  };

  const $ = (sel) => document.querySelector(sel);
  const content = $('#content');
  const contentScroll = $('#content-scroll');
  const sourceScroll = $('#source-scroll');
  const sourceText = $('#source-text');

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
      case 'load':
        renderDocument(msg.html, msg.source, msg.headings, msg.settings, msg.stats, msg.imageBase);
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
      case 'menu-opened':
        state.menuOpen = true;
        break;
      case 'menu-closed':
        state.menuOpen = false;
        break;
    }
  }

  // ---------- Рендеринг документа ----------
  function renderDocument(html, source, headings, settings, stats, imageBase) {
    state.html = html || '';
    state.source = source || '';
    state.headings = headings || [];
    state.settings = settings || {};

    // Санитизация через DOMPurify (защита от встроенного враждебного HTML)
    if (window.DOMPurify) {
      content.innerHTML = DOMPurify.sanitize(state.html, {
        ADD_ATTR: ['target', 'rel', 'class', 'id'],
        ADD_TAGS: ['input'],            // чекбоксы списков задач
        FORBID_TAGS: ['script', 'style', 'iframe', 'object', 'embed', 'form'],
      });
    } else {
      content.innerHTML = state.html;
    }

    // Локальные картинки документа: относительные пути раздаются
    // виртуальным хостом media.markdownlister (папка документа)
    rewriteImageUrls(imageBase);

    sourceText.textContent = state.source;
    applySettings(state.settings);
    buildToc();
    enhanceCodeBlocks();
    renderMath();
    renderMermaid();
    updateStats(stats);
    contentScroll.scrollTop = 0;
    clearSearch();
  }

  // Переписывает относительные пути картинок на виртуальный хост папки
  // документа (imageBase). Абсолютные URL, data: и якоря не трогаем.
  function rewriteImageUrls(imageBase) {
    if (!imageBase) return;
    content.querySelectorAll('img').forEach((img) => {
      const src = img.getAttribute('src') || '';
      if (!src || /^(https?:|data:|blob:|\/\/|#)/i.test(src)) return;
      // Абсолютные пути файловой системы не отдаются через хост папки документа
      if (/^([a-z]:)?[/\\]/i.test(src)) return;
      img.src = imageBase + encodeURI(src.replace(/\\/g, '/'));
    });
  }

  // ---------- Настройки / темы / зум ----------
  function applySettings(s) {
    if (!s) return;
    state.settings = Object.assign({}, state.settings, s);
    setTheme(s.theme || 'System');
    setZoom(s.zoom || 100);
    document.documentElement.style.setProperty('--reading-width', (s.readingWidth || 900) + 'px');
    $('#sidebar').hidden = !s.showToc;
    sourceScroll.hidden = !s.splitView;
    document.body.classList.toggle('no-line-numbers', s.showLineNumbers === false);
    if (s.codeTheme) setCodeTheme(s.codeTheme);
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

  function runSearch() {
    clearSearch();
    const q = $('#search-input').value;
    if (!q) return;

    const needle = q.toLowerCase();
    // Обход текстовых узлов (без <script>/<style> — их уже нет после санитизации)
    const walker = document.createTreeWalker(content, NodeFilter.SHOW_TEXT, null);
    const targets = [];
    let node;
    while ((node = walker.nextNode())) {
      if (node.nodeValue && node.nodeValue.toLowerCase().includes(needle)) {
        targets.push(node);
      }
    }

    targets.forEach((textNode) => {
      const text = textNode.nodeValue;
      const lower = text.toLowerCase();
      const frag = document.createDocumentFragment();
      let pos = 0;
      let idx;
      while ((idx = lower.indexOf(needle, pos)) !== -1) {
        if (idx > pos) frag.appendChild(document.createTextNode(text.substring(pos, idx)));
        const mark = document.createElement('mark');
        mark.className = 'search-hit';
        mark.textContent = text.substr(idx, needle.length);
        frag.appendChild(mark);
        pos = idx + needle.length;
      }
      if (pos < text.length) frag.appendChild(document.createTextNode(text.substring(pos)));
      textNode.parentNode.replaceChild(frag, textNode);
    });

    state.searchHits = Array.from(content.querySelectorAll('mark.search-hit'));
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
    state.searchHits.forEach((m, i) => m.classList.toggle('current', i === state.searchIndex));
    const cur = state.searchHits[state.searchIndex];
    if (cur) cur.scrollIntoView({ behavior: 'smooth', block: 'center' });
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
    if (hidden) syncScroll(contentScroll, sourceScroll);
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
    $('#status-words').textContent = plural(stats.words, ['слово', 'слова', 'слов']);
    $('#status-time').textContent = '~' + stats.readingTime + ' мин';
    $('#status-headings').textContent = plural(stats.headings, ['заголовок', 'заголовка', 'заголовков']);
  }

  function plural(n, forms) {
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
      btn.textContent = 'Копировать';
      btn.addEventListener('click', () => {
        const text = code.textContent;
        if (navigator.clipboard) {
          navigator.clipboard.writeText(text).then(() => {
            btn.textContent = 'Скопировано!';
            setTimeout(() => { btn.textContent = 'Копировать'; }, 1500);
          });
        } else {
          // Фолбэк для старых движков
          const ta = document.createElement('textarea');
          ta.value = text;
          document.body.appendChild(ta);
          ta.select();
          document.execCommand('copy');
          document.body.removeChild(ta);
          btn.textContent = 'Скопировано!';
          setTimeout(() => { btn.textContent = 'Копировать'; }, 1500);
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
      s.onerror = () => reject(new Error('Не удалось загрузить ' + src));
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
            b.textContent = 'Диаграмма Mermaid не отрисована: библиотека mermaid не загрузилась.';
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
              b.textContent = 'Ошибка диаграммы Mermaid: ' + (e && e.message ? e.message : String(e));
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
    content.innerHTML = '<div style="padding:2em;text-align:center;color:#c0392b;">' +
      '<h2>Ошибка</h2><p></p></div>';
    content.querySelector('p').textContent = text || 'Неизвестная ошибка';
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
      if (!$('#search-bar').hidden) closeSearch();
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
