(function () {
  'use strict';

  const sessions = new Map();
  let activeId = null;
  let currentFontSize = 14;
  let currentFontFamily = '"JetBrains Mono", "Cascadia Mono", "Consolas", "Microsoft YaHei UI", monospace';
  let currentLineHeight = 1.25;
  let currentScrollback = 2000;
  let currentTheme = null;
  let hostReady = false;

  const stack = document.getElementById('terminal-stack');
  const menu = document.getElementById('context-menu');

  function send(message) {
    if (!hostReady || !window.chrome || !window.chrome.webview) return;
    window.chrome.webview.postMessage(message);
  }

  function decodeBase64(base64) {
    const binary = atob(base64);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
    return bytes;
  }

  function currentCommandLine(term) {
    const buffer = term.buffer.active;
    const line = buffer.getLine(buffer.baseY + buffer.cursorY);
    if (!line) return '';

    // Do not rely on a prompt ending in #/$/%/>. zsh themes often use
    // symbols such as ❯ or ➜, or put a right prompt after the command.
    // Find the last cd/pushd token preceded by whitespace/semicolon instead.
    const text = line.translateToString(true).replace(/\u00a0/g, ' ').trimEnd();
    const match = text.match(/(?:^|[\s;>%#$❯➜])((?:cd|pushd)(?:\s+.*)?)$/i);
    return match ? match[1].trim() : '';
  }

  function createTerminal(id, fontSize, scrollback, fontFamily, lineHeight) {
    if (sessions.has(id)) return sessions.get(id);

    const wrapper = document.createElement('div');
    wrapper.className = 'term-wrap';
    wrapper.dataset.sessionId = id;
    stack.appendChild(wrapper);

    const term = new Terminal({
      fontFamily: fontFamily || currentFontFamily,
      fontSize: fontSize || currentFontSize,
      lineHeight: lineHeight || currentLineHeight,
      letterSpacing: 0,
      scrollback: scrollback || currentScrollback,
      cursorBlink: true,
      cursorStyle: 'bar',
      cursorWidth: 2,
      allowTransparency: false,
      allowProposedApi: true,
      macOptionIsMeta: true,
      rightClickSelectsWord: false,
      theme: currentTheme || undefined,
      windowsPty: { backend: 'conpty' }
    });

    const fit = new FitAddon.FitAddon();
    term.loadAddon(fit);
    term.open(wrapper);

    term.onData(function (data) {
      // Keep the prompt/input visible even if the user had scrolled back.
      try { term.scrollToBottom(); } catch (_) { }
      send({ type: 'input', sessionId: id, data: data });
      if (data === '\r' || data === '\n') {
        const commandLine = currentCommandLine(term);
        if (commandLine) send({ type: 'commandLine', sessionId: id, line: commandLine });
      }
    });
    term.onResize(function (size) {
      send({ type: 'resize', sessionId: id, cols: size.cols, rows: size.rows });
    });
    term.onTitleChange(function (title) {
      send({ type: 'title', sessionId: id, title: title });
    });
    try {
      term.parser.registerOscHandler(7, function (data) {
        let path = data;
        if (data.startsWith('file://')) {
          try { path = decodeURIComponent(new URL(data).pathname); } catch (_) { return false; }
        }
        if (path && path.charAt(0) === '/') {
          send({ type: 'cwd', sessionId: id, path: path });
          return true;
        }
        return false;
      });
    } catch (_) { }
    term.attachCustomKeyEventHandler(function (event) {
      if (event.type !== 'keydown') return true;
      if (event.ctrlKey && event.shiftKey && event.code === 'KeyC') {
        copySelection(id);
        return false;
      }
      if (event.ctrlKey && event.shiftKey && event.code === 'KeyV') {
        pasteClipboard(id);
        return false;
      }
      // Plain Ctrl+C copies when there is a selection; otherwise it must
      // continue to the shell as SIGINT (^C).
      if (event.ctrlKey && !event.shiftKey && !event.altKey && event.code === 'KeyC') {
        if (term.hasSelection()) {
          copySelection(id);
          return false;
        }
        return true;
      }
      // Plain Ctrl+V is convenient for pasting from Windows clipboard. The
      // Ctrl+Shift+V path above remains available as the terminal-safe form.
      if (event.ctrlKey && !event.shiftKey && !event.altKey && event.code === 'KeyV') {
        pasteClipboard(id);
        return false;
      }
      if (event.ctrlKey && !event.shiftKey && !event.altKey && event.code === 'Insert') {
        if (term.hasSelection()) {
          copySelection(id);
          return false;
        }
        return true;
      }
      if (event.shiftKey && !event.ctrlKey && !event.altKey && event.code === 'Insert') {
        pasteClipboard(id);
        return false;
      }
      if (event.ctrlKey && event.shiftKey && event.code === 'KeyA') {
        term.selectAll();
        return false;
      }
      if (event.ctrlKey && event.shiftKey && event.code === 'KeyL') {
        term.clear();
        return false;
      }
      return true;
    });

    const record = { term: term, fit: fit, wrapper: wrapper, title: '' };
    sessions.set(id, record);
    return record;
  }

  function activate(id) {
    activeId = id;
    for (const [sessionId, record] of sessions) {
      record.wrapper.classList.toggle('active', sessionId === id);
    }
    const record = sessions.get(id);
    if (!record) return;
    requestAnimationFrame(function () {
      try { record.fit.fit(); } catch (_) { }
      scrollToBottom(record);
      record.term.focus();
    });
  }

  function fitActive() {
    if (!activeId) return;
    const record = sessions.get(activeId);
    if (!record) return;
    try { record.fit.fit(); } catch (_) { }
    scrollToBottom(record);
  }

  function scrollToBottom(record) {
    try { record.term.scrollToBottom(); } catch (_) { }
  }

  function writeSession(id, base64) {
    const record = sessions.get(id);
    if (!record) return;
    try {
      record.term.write(decodeBase64(base64), function () { scrollToBottom(record); });
      scrollToBottom(record);
    } catch (_) { }
  }

  function clearSession(id) {
    const record = sessions.get(id);
    if (record) {
      record.term.clear();
      record.term.write('\x1b[2J\x1b[H');
    }
  }

  function closeSession(id) {
    const record = sessions.get(id);
    if (!record) return;
    try { record.term.dispose(); } catch (_) { }
    record.wrapper.remove();
    sessions.delete(id);
    if (activeId === id) {
      activeId = sessions.size ? sessions.keys().next().value : null;
      if (activeId) activate(activeId);
    }
  }

  function applyTheme(theme, background, menuBackground, menuBorder, menuHover, menuText, selection) {
    currentTheme = theme;
    document.documentElement.style.setProperty('--term-bg', background);
    document.documentElement.style.setProperty('--menu-bg', menuBackground);
    document.documentElement.style.setProperty('--menu-border', menuBorder);
    document.documentElement.style.setProperty('--menu-hover', menuHover);
    document.documentElement.style.setProperty('--menu-text', menuText);
    document.documentElement.style.setProperty('--term-selection', selection);
    for (const record of sessions.values()) {
      record.term.options.theme = Object.assign({}, theme);
      try {
        if (record.term.clearTextureAtlas) record.term.clearTextureAtlas();
        record.term.refresh(0, Math.max(0, record.term.rows - 1));
        record.fit.fit();
        scrollToBottom(record);
        // A second refresh after layout covers cells that were visible while
        // the theme message arrived (xterm caches glyph foreground colors).
        requestAnimationFrame(function () {
          try {
            record.term.refresh(0, Math.max(0, record.term.rows - 1));
            record.fit.fit();
            scrollToBottom(record);
          } catch (_) { }
        });
      } catch (_) { }
    }
  }

  function applyFontSize(size) {
    currentFontSize = size;
    for (const record of sessions.values()) {
      record.term.options.fontSize = size;
      try { record.fit.fit(); } catch (_) { }
      scrollToBottom(record);
    }
  }

  function applyFont(fontFamily, fontSize, lineHeight) {
    if (fontFamily) currentFontFamily = fontFamily;
    if (fontSize) currentFontSize = fontSize;
    if (lineHeight) currentLineHeight = lineHeight;
    for (const record of sessions.values()) {
      record.term.options.fontFamily = currentFontFamily;
      record.term.options.fontSize = currentFontSize;
      record.term.options.lineHeight = currentLineHeight;
      try { record.fit.fit(); } catch (_) { }
      scrollToBottom(record);
    }
  }

  function selectedText() {
    if (!activeId) return '';
    const record = sessions.get(activeId);
    return record ? record.term.getSelection() : '';
  }

  function copySelection(id) {
    const record = sessions.get(id || activeId);
    if (!record) return;
    const text = record.term.getSelection();
    if (!text) return;
    writeClipboard(text).then(function () {
      record.term.focus();
    });
  }

  function writeClipboard(text) {
    if (navigator.clipboard && navigator.clipboard.writeText) {
      return navigator.clipboard.writeText(text).catch(function () { fallbackCopy(text); });
    }
    fallbackCopy(text);
    return Promise.resolve();
  }

  function fallbackCopy(text) {
    const area = document.createElement('textarea');
    area.value = text;
    area.style.position = 'fixed';
    area.style.opacity = '0';
    document.body.appendChild(area);
    area.select();
    try { document.execCommand('copy'); } catch (_) { }
    area.remove();
  }

  function pasteClipboard(id) {
    const record = sessions.get(id || activeId);
    if (!record) return;
    const finish = function (text) {
      if (text) record.term.paste(text);
      record.term.focus();
    };
    if (navigator.clipboard && navigator.clipboard.readText) {
      navigator.clipboard.readText().then(finish).catch(function () { send({ type: 'pasteRequest', sessionId: id || activeId }); });
    } else {
      send({ type: 'pasteRequest', sessionId: id || activeId });
    }
  }

  function hideMenu() {
    menu.classList.remove('visible');
    menu.setAttribute('aria-hidden', 'true');
  }

  function showMenu(x, y) {
    menu.classList.add('visible');
    menu.setAttribute('aria-hidden', 'false');
    const width = menu.offsetWidth;
    const height = menu.offsetHeight;
    menu.style.left = Math.min(x, window.innerWidth - width - 6) + 'px';
    menu.style.top = Math.min(y, window.innerHeight - height - 6) + 'px';
  }

  document.addEventListener('contextmenu', function (event) {
    if (!event.target.closest('.term-wrap')) return;
    event.preventDefault();
    showMenu(event.clientX, event.clientY);
  });

  document.addEventListener('mousedown', function (event) {
    if (!event.target.closest('#context-menu')) hideMenu();
  });

  window.addEventListener('blur', hideMenu);
  window.addEventListener('resize', fitActive);

  let lastFontWheelAt = 0;
  document.addEventListener('wheel', function (event) {
    if (!event.ctrlKey || !event.target.closest('.term-wrap')) return;
    event.preventDefault();
    const now = Date.now();
    if (now - lastFontWheelAt < 60) return;
    lastFontWheelAt = now;
    const direction = event.deltaY < 0 ? 1 : event.deltaY > 0 ? -1 : 0;
    const next = Math.min(26, Math.max(10, currentFontSize + direction));
    if (!direction || next === currentFontSize) return;
    applyFontSize(next);
    send({ type: 'fontSizeChanged', fontSize: next });
  }, { passive: false });

  if (window.ResizeObserver) {
    new ResizeObserver(function () { fitActive(); }).observe(stack);
  }

  menu.addEventListener('click', function (event) {
    const button = event.target.closest('button[data-action]');
    if (!button) return;
    const action = button.dataset.action;
    if (action === 'copy') copySelection(activeId);
    else if (action === 'paste') pasteClipboard(activeId);
    else if (action === 'selectall') {
      const record = sessions.get(activeId);
      if (record) { record.term.selectAll(); record.term.focus(); }
    } else if (action === 'clear') clearSession(activeId);
    hideMenu();
  });

  window.chrome.webview.addEventListener('message', function (event) {
    const message = event.data;
    if (!message || !message.type) return;
    switch (message.type) {
      case 'create':
        createTerminal(message.sessionId, message.fontSize, message.scrollback,
          message.fontFamily, message.lineHeight);
        break;
      case 'activate':
        activate(message.sessionId);
        break;
      case 'write':
        writeSession(message.sessionId, message.data);
        break;
      case 'clear':
        clearSession(message.sessionId);
        break;
      case 'close':
        closeSession(message.sessionId);
        break;
      case 'focus':
        if (activeId && sessions.has(activeId)) sessions.get(activeId).term.focus();
        break;
      case 'fit':
        fitActive();
        break;
      case 'theme':
        applyTheme(message.theme, message.background, message.menuBackground,
          message.menuBorder, message.menuHover, message.menuText, message.selection);
        break;
      case 'fontSize':
        applyFontSize(message.fontSize);
        break;
      case 'font':
        applyFont(message.fontFamily, message.fontSize, message.lineHeight);
        break;
      case 'paste':
        {
          const record = sessions.get(message.sessionId);
          if (record) record.term.paste(message.text || '');
        }
        break;
    }
  });

  hostReady = true;
  send({ type: 'ready' });
})();
