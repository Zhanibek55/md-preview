// Runs inside the preview page. Loads heavy libraries only when the document needs them.
(function () {
  'use strict';

  var ASSETS = '/__mdp_assets__/';
  var root = document.documentElement;
  var theme = root.getAttribute('data-theme') === 'dark' ? 'dark' : 'light';
  var started = performance.now();

  function post(message) {
    try {
      window.chrome.webview.postMessage(message);
    } catch (e) {
      // Not running inside WebView2 (opened in a normal browser for debugging).
    }
  }

  window.addEventListener('error', function (e) {
    post({ type: 'error', message: String(e.message), source: String(e.filename || ''), line: e.lineno || 0 });
  });
  window.addEventListener('unhandledrejection', function (e) {
    post({ type: 'error', message: String(e.reason && e.reason.message || e.reason) });
  });

  function loadScript(src) {
    return new Promise(function (resolve, reject) {
      var s = document.createElement('script');
      s.src = ASSETS + src;
      s.onload = resolve;
      s.onerror = function () { reject(new Error('failed to load ' + src)); };
      document.head.appendChild(s);
    });
  }

  function loadStyle(href) {
    var l = document.createElement('link');
    l.rel = 'stylesheet';
    l.href = ASSETS + href;
    document.head.appendChild(l);
  }

  function markFailed(el, text) {
    el.classList.add('mdp-error');
    var note = document.createElement('div');
    note.className = 'mdp-error-note';
    note.textContent = text;
    el.parentNode.insertBefore(note, el);
  }

  function addCopyButtons() {
    var blocks = document.querySelectorAll('article pre:not(.mermaid)');
    blocks.forEach(function (pre) {
      var button = document.createElement('button');
      button.type = 'button';
      button.className = 'mdp-copy';
      button.textContent = 'Copy';
      button.addEventListener('click', function () {
        var code = pre.querySelector('code') || pre;
        navigator.clipboard.writeText(code.innerText).then(function () {
          button.textContent = 'Copied';
          setTimeout(function () { button.textContent = 'Copy'; }, 1200);
        }, function () {
          button.textContent = 'Copy failed';
        });
      });
      pre.appendChild(button);
    });
  }

  // In-document links. Tables of contents written by people and AI tools often do not match
  // the generated heading ids exactly: "## 🎯 Обзор" gets id "-обзор" (GitHub rules) while the
  // link says "#обзор". Resolve such links tolerantly instead of doing nothing.
  function normalize(s) {
    return String(s || '').normalize('NFKC').toLowerCase().replace(/[^\p{L}\p{N}]+/gu, '');
  }

  function findAnchorTarget(fragment) {
    var name = fragment;
    try { name = decodeURIComponent(fragment); } catch (e) { /* keep raw */ }
    if (!name) return document.body;

    var exact = document.getElementById(name) || document.getElementById('-' + name) ||
      document.getElementsByName(name)[0];
    if (exact) return exact;

    var wanted = normalize(name);
    if (!wanted) return null;

    var withIds = document.querySelectorAll('article [id]');
    for (var i = 0; i < withIds.length; i++) {
      if (normalize(withIds[i].id) === wanted) return withIds[i];
    }

    var headings = document.querySelectorAll('article h1, article h2, article h3, article h4, article h5, article h6');
    for (var j = 0; j < headings.length; j++) {
      if (normalize(headings[j].textContent) === wanted) return headings[j];
    }
    for (var k = 0; k < headings.length; k++) {
      var text = normalize(headings[k].textContent);
      if (text && (text.indexOf(wanted) === 0 || wanted.indexOf(text) === 0)) return headings[k];
    }
    return null;
  }

  function flash(el) {
    el.classList.remove('mdp-flash');
    void el.offsetWidth; // restart the animation
    el.classList.add('mdp-flash');
  }

  document.addEventListener('click', function (e) {
    var link = e.target && e.target.closest ? e.target.closest('a[href^="#"]') : null;
    if (!link || e.defaultPrevented || e.button !== 0) return;
    e.preventDefault();
    var href = link.getAttribute('href');
    var target = findAnchorTarget(href.slice(1));
    if (!target) {
      post({ type: 'anchor-missing', href: href });
      return;
    }
    target.scrollIntoView({ block: 'start' });
    if (target !== document.body) {
      flash(target);
      var prev = document.querySelector('[data-mdp-anchored]');
      if (prev) prev.removeAttribute('data-mdp-anchored');
      target.setAttribute('data-mdp-anchored', '');
    }
    post({ type: 'anchor', href: href, target: target.id || target.tagName });
  });

  var tasks = [];
  var stats = { code: 0, math: 0, mermaid: 0, failed: 0 };

  // Syntax highlighting. Blocks with a known language are highlighted by it; blocks with no
  // language are auto-detected (when they are long enough that a guess is meaningful).
  var PLAIN = { text: 1, plaintext: 1, plain: 1, txt: 1, none: 1, output: 1, console: 1, log: 1 };
  var codes = Array.prototype.slice.call(document.querySelectorAll('article pre:not(.mermaid) > code'));
  stats.code = codes.length;
  if (codes.length) {
    loadStyle(theme === 'dark' ? 'vendor/highlight/github-dark.min.css' : 'vendor/highlight/github.min.css');
    tasks.push(loadScript('vendor/highlight/highlight.min.js').then(function () {
      codes.forEach(function (code) {
        var m = /language-([\w+#.-]+)/.exec(code.className);
        var lang = m ? m[1].toLowerCase() : null;
        if (lang && PLAIN[lang]) return;
        if (lang && window.hljs.getLanguage(lang)) {
          window.hljs.highlightElement(code);
        } else if (!lang) {
          var txt = code.textContent || '';
          if (txt.trim().length >= 12 && /\n/.test(txt)) {
            try { window.hljs.highlightElement(code); } catch (e) { /* leave plain */ }
          }
        }
      });
    }));
  }

  // Math: Markdig emits <span class="math">\(..\)</span> and <div class="math">\[..\]</div>.
  var maths = Array.prototype.slice.call(document.querySelectorAll('.math'));
  stats.math = maths.length;
  if (maths.length) {
    loadStyle('vendor/katex/katex.min.css');
    tasks.push(loadScript('vendor/katex/katex.min.js').then(function () {
      maths.forEach(function (el) {
        var display = el.tagName === 'DIV';
        var tex = el.textContent.trim()
          .replace(/^\\\(|^\\\[/, '')
          .replace(/\\\)$|\\\]$/, '')
          .trim();
        try {
          window.katex.render(tex, el, { displayMode: display, throwOnError: false, trust: false });
        } catch (e) {
          stats.failed++;
          markFailed(el, 'Formula error: ' + e.message);
        }
      });
    }));
  }

  // Mermaid diagrams: Markdig emits <pre class="mermaid">source</pre>.
  var diagrams = Array.prototype.slice.call(document.querySelectorAll('.mermaid'));
  stats.mermaid = diagrams.length;
  if (diagrams.length) {
    tasks.push(loadScript('vendor/mermaid/mermaid.min.js').then(function () {
      window.mermaid.initialize({
        startOnLoad: false,
        securityLevel: 'strict',
        theme: theme === 'dark' ? 'dark' : 'default',
      });
      var chain = Promise.resolve();
      diagrams.forEach(function (el) {
        var source = el.textContent;
        chain = chain.then(function () {
          // Parse first: on a syntax error keep the source visible with a short note
          // instead of Mermaid's large error picture.
          return window.mermaid.parse(source).then(function () {
            return window.mermaid.run({ nodes: [el] });
          }).catch(function (e) {
            stats.failed++;
            el.textContent = source;
            el.removeAttribute('data-processed');
            var lines = String(e && e.message || e).split('\n').filter(function (l) { return l.trim(); });
            var text = lines[0] + (lines.length > 1 ? ' ' + lines[lines.length - 1] : '');
            markFailed(el, 'Diagram error: ' + text);
          });
        });
      });
      return chain;
    }));
  }

  // Remote images are blocked by the page's Content-Security-Policy unless the user opted in.
  // When blocked, replace them with a small inline note so there is no broken-image icon.
  function handleRemoteImages() {
    if (root.getAttribute('data-remote-images') === '1') return;
    var imgs = document.querySelectorAll('article img');
    Array.prototype.forEach.call(imgs, function (img) {
      var src = img.getAttribute('src') || '';
      if (!/^https?:\/\//i.test(src)) return;
      var note = document.createElement('span');
      note.className = 'mdp-remote-img';
      note.title = src;
      note.textContent = '🔒 ' + (img.getAttribute('alt') || 'remote image') + ' (hidden)';
      if (img.parentNode) img.parentNode.replaceChild(note, img);
    });
  }

  // Floating outline (table of contents) built from the document's headings, with scroll-spy.
  function buildOutline() {
    var headings = Array.prototype.slice.call(
      document.querySelectorAll('article h1, article h2, article h3, article h4, article h5, article h6'))
      .filter(function (h) { return h.textContent.trim(); });
    if (headings.length < 3) return;
    headings.forEach(function (h, i) { if (!h.id) h.id = 'mdp-h-' + i; });
    var minLevel = Math.min.apply(null, headings.map(function (h) { return +h.tagName.charAt(1); }));

    var panel = document.createElement('nav');
    panel.className = 'mdp-outline';
    panel.setAttribute('aria-label', 'Outline');
    var list = document.createElement('ul');
    var entries = headings.map(function (h) {
      var li = document.createElement('li');
      li.className = 'mdp-o-l' + Math.min(3, +h.tagName.charAt(1) - minLevel);
      var a = document.createElement('a');
      a.href = '#' + h.id;
      a.textContent = h.textContent;
      li.appendChild(a);
      list.appendChild(li);
      return { h: h, a: a };
    });
    panel.appendChild(list);

    var toggle = document.createElement('button');
    toggle.type = 'button';
    toggle.className = 'mdp-outline-toggle';
    toggle.title = 'Outline';
    toggle.setAttribute('aria-label', 'Outline');
    toggle.innerHTML = '<svg viewBox="0 0 16 16" width="16" height="16" aria-hidden="true">' +
      '<rect x="1" y="2" width="3" height="2"/><rect x="6" y="2.5" width="9" height="1"/>' +
      '<rect x="1" y="7" width="3" height="2"/><rect x="6" y="7.5" width="9" height="1"/>' +
      '<rect x="1" y="12" width="3" height="2"/><rect x="6" y="12.5" width="9" height="1"/></svg>';

    function setOpen(open) {
      document.body.classList.toggle('mdp-outline-open', open);
      toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
    }
    toggle.addEventListener('click', function () {
      setOpen(!document.body.classList.contains('mdp-outline-open'));
    });
    panel.addEventListener('click', function (e) {
      // The document-level handler does the scroll; here we just close on a narrow pane.
      if (e.target.closest && e.target.closest('a') && window.innerWidth < 1000) setOpen(false);
    });

    document.body.appendChild(toggle);
    document.body.appendChild(panel);
    setOpen(window.innerWidth >= 1000);

    var ticking = false;
    function spy() {
      ticking = false;
      var best = entries[0];
      for (var i = 0; i < entries.length; i++) {
        if (entries[i].h.getBoundingClientRect().top <= 84) best = entries[i]; else break;
      }
      entries.forEach(function (en) { en.a.classList.toggle('active', en === best); });
      if (best && document.body.classList.contains('mdp-outline-open')) {
        en_scroll(best.a);
      }
    }
    function en_scroll(a) {
      var pr = panel.getBoundingClientRect(), ar = a.getBoundingClientRect();
      if (ar.top < pr.top + 8 || ar.bottom > pr.bottom - 8) a.scrollIntoView({ block: 'nearest' });
    }
    window.addEventListener('scroll', function () {
      if (!ticking) { ticking = true; requestAnimationFrame(spy); }
    }, { passive: true });
    spy();
    window.__mdpOutline = { count: entries.length };
  }

  // Find in page (Ctrl+F): a small search bar that highlights matches with the CSS Highlight API.
  function setupFind() {
    var bar = document.createElement('div');
    bar.className = 'mdp-find';
    bar.hidden = true;
    bar.innerHTML =
      '<input type="text" class="mdp-find-input" placeholder="Find" aria-label="Find in page">' +
      '<span class="mdp-find-count" aria-live="polite"></span>' +
      '<button type="button" class="mdp-find-prev" title="Previous (Shift+Enter)" aria-label="Previous">↑</button>' +
      '<button type="button" class="mdp-find-next" title="Next (Enter)" aria-label="Next">↓</button>' +
      '<button type="button" class="mdp-find-close" title="Close (Esc)" aria-label="Close">×</button>';
    document.body.appendChild(bar);

    var input = bar.querySelector('.mdp-find-input');
    var count = bar.querySelector('.mdp-find-count');
    var supported = typeof Highlight !== 'undefined' && window.CSS && CSS.highlights;
    var matches = [];
    var current = -1;

    function clearHighlights() {
      if (supported) { CSS.highlights.delete('mdp-find'); CSS.highlights.delete('mdp-find-current'); }
      matches = [];
      current = -1;
    }

    function textNodes() {
      var article = document.querySelector('article');
      var walker = document.createTreeWalker(article, NodeFilter.SHOW_TEXT, {
        acceptNode: function (n) {
          if (!n.nodeValue || !n.nodeValue.trim()) return NodeFilter.FILTER_REJECT;
          var p = n.parentElement;
          if (!p || (p.closest && p.closest('.mdp-find, .mdp-outline, script, style'))) return NodeFilter.FILTER_REJECT;
          return NodeFilter.FILTER_ACCEPT;
        }
      });
      var out = [], n;
      while ((n = walker.nextNode())) out.push(n);
      return out;
    }

    function search(q) {
      clearHighlights();
      if (!q) { count.textContent = ''; return 0; }
      if (!supported) { count.textContent = 'n/a'; return 0; }
      var needle = q.toLowerCase();
      var all = new Highlight();
      var nodes = textNodes();
      for (var i = 0; i < nodes.length && matches.length < 2000; i++) {
        var hay = nodes[i].nodeValue.toLowerCase();
        var idx = 0;
        while ((idx = hay.indexOf(needle, idx)) !== -1) {
          var r = document.createRange();
          r.setStart(nodes[i], idx);
          r.setEnd(nodes[i], idx + needle.length);
          matches.push(r);
          all.add(r);
          idx += needle.length;
          if (matches.length >= 2000) break;
        }
      }
      CSS.highlights.set('mdp-find', all);
      if (matches.length) { current = 0; showCurrent(); }
      else { count.textContent = '0/0'; }
      return matches.length;
    }

    function showCurrent() {
      if (!matches.length) { count.textContent = '0/0'; return; }
      count.textContent = (current + 1) + '/' + matches.length;
      CSS.highlights.set('mdp-find-current', new Highlight(matches[current]));
      var node = matches[current].startContainer;
      var el = node.nodeType === 3 ? node.parentElement : node;
      if (el) el.scrollIntoView({ block: 'center' });
    }

    function move(delta) {
      if (!matches.length) return;
      current = (current + delta + matches.length) % matches.length;
      showCurrent();
    }

    function open() { bar.hidden = false; input.focus(); input.select(); }
    function close() { bar.hidden = true; clearHighlights(); count.textContent = ''; }

    input.addEventListener('input', function () { search(input.value); });
    input.addEventListener('keydown', function (e) {
      if (e.key === 'Enter') { e.preventDefault(); move(e.shiftKey ? -1 : 1); }
      else if (e.key === 'Escape') { e.preventDefault(); close(); }
    });
    bar.querySelector('.mdp-find-next').addEventListener('click', function () { move(1); });
    bar.querySelector('.mdp-find-prev').addEventListener('click', function () { move(-1); });
    bar.querySelector('.mdp-find-close').addEventListener('click', close);

    document.addEventListener('keydown', function (e) {
      if ((e.ctrlKey || e.metaKey) && !e.altKey && (e.key === 'f' || e.key === 'F')) {
        e.preventDefault();
        open();
      }
    });

    // Test hook.
    window.__mdpFind = {
      search: search, open: open, close: close, supported: supported,
      count: function () { return matches.length; }
    };
  }

  handleRemoteImages();
  buildOutline();
  setupFind();
  addCopyButtons();

  Promise.all(tasks.map(function (t) {
    return t.catch(function (e) {
      stats.failed++;
      post({ type: 'error', message: String(e && e.message || e) });
    });
  })).then(function () {
    root.setAttribute('data-rendered', '1');
    post({
      type: 'rendered',
      ms: Math.round(performance.now() - started),
      code: stats.code,
      math: stats.math,
      mermaid: stats.mermaid,
      failed: stats.failed,
    });
  });
})();
