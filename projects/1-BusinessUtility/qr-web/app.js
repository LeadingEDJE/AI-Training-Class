/**
 * QR Code Generator
 *
 * Renders QR codes for URLs on a canvas, exports them as PNG or SVG, and keeps
 * a short history in localStorage. Encoding is handled by the bundled
 * qrcode-generator library, which exposes the global `qrcode(type, ecc)`.
 */
(function () {
  'use strict';

  var HISTORY_KEY = 'qr-web.history';
  var HISTORY_LIMIT = 12;
  var THUMB_SIZE = 160;
  var SCHEME_RE = /^[a-z][a-z0-9+.\-]*:/i;
  var HOSTLIKE_RE = /^[^\s/]+\.[^\s/]{2,}/;

  // ---------- Elements ----------
  var el = {
    form: document.getElementById('qr-form'),
    url: document.getElementById('url'),
    urlError: document.getElementById('url-error'),
    size: document.getElementById('size'),
    ecc: document.getElementById('ecc'),
    dark: document.getElementById('dark'),
    light: document.getElementById('light'),
    margin: document.getElementById('margin'),
    marginOut: document.getElementById('margin-out'),
    canvas: document.getElementById('canvas'),
    placeholder: document.getElementById('placeholder'),
    meta: document.getElementById('meta'),
    metaUrl: document.getElementById('meta-url'),
    metaVersion: document.getElementById('meta-version'),
    metaModules: document.getElementById('meta-modules'),
    savePng: document.getElementById('save-png'),
    saveSvg: document.getElementById('save-svg'),
    copy: document.getElementById('copy'),
    status: document.getElementById('status'),
    history: document.getElementById('history'),
    historyEmpty: document.getElementById('history-empty'),
    clearHistory: document.getElementById('clear-history')
  };

  /** The most recently rendered code, or null when the preview is empty. */
  var current = null;

  // ---------- URL handling ----------

  /**
   * Turn what the user typed into the string we encode. A bare domain like
   * "example.com/page" gets an https:// prefix; anything else is left alone so
   * plain text and other schemes (mailto:, tel:) still work.
   */
  function normalize(raw) {
    var text = raw.trim();
    if (!text) return '';
    if (SCHEME_RE.test(text)) return text;
    if (HOSTLIKE_RE.test(text)) return 'https://' + text;
    return text;
  }

  /** A filename-safe stem for downloads, based on the encoded value. */
  function filenameFor(text) {
    var stem = text;
    try {
      var parsed = new URL(text);
      stem = parsed.hostname + parsed.pathname;
    } catch (err) {
      /* Not a URL - fall back to the raw text. */
    }
    stem = stem.replace(/[^a-z0-9]+/gi, '-').replace(/^-+|-+$/g, '').toLowerCase();
    return 'qr-' + (stem || 'code').slice(0, 40);
  }

  // ---------- Encoding and drawing ----------

  /** Encode `text` and return the QR model, or throw if it will not fit. */
  function encode(text, ecc) {
    var qr = qrcode(0, ecc); // type 0 picks the smallest version that fits
    qr.addData(text);
    qr.make();
    return qr;
  }

  /**
   * Paint a QR model onto a canvas at `size` pixels square.
   *
   * Module width is rounded down to a whole number of pixels and the result is
   * centred, so every module lands on exact pixel boundaries and the code stays
   * crisp at any requested size.
   */
  function draw(canvas, qr, opts) {
    var count = qr.getModuleCount();
    var total = count + opts.margin * 2;
    var scale = Math.max(1, Math.floor(opts.size / total));
    var drawn = scale * total;
    var size = Math.max(opts.size, drawn);
    var offset = Math.floor((size - drawn) / 2);

    canvas.width = size;
    canvas.height = size;

    var ctx = canvas.getContext('2d');
    ctx.fillStyle = opts.light;
    ctx.fillRect(0, 0, size, size);
    ctx.fillStyle = opts.dark;

    for (var row = 0; row < count; row++) {
      for (var col = 0; col < count; col++) {
        if (!qr.isDark(row, col)) continue;
        ctx.fillRect(
          offset + (col + opts.margin) * scale,
          offset + (row + opts.margin) * scale,
          scale,
          scale
        );
      }
    }
  }

  /** Build a standalone SVG string. Coordinates are in module units. */
  function toSvg(qr, opts) {
    var count = qr.getModuleCount();
    var total = count + opts.margin * 2;
    var path = '';

    for (var row = 0; row < count; row++) {
      for (var col = 0; col < count; col++) {
        if (qr.isDark(row, col)) {
          path += 'M' + (col + opts.margin) + ' ' + (row + opts.margin) + 'h1v1h-1z';
        }
      }
    }

    return '<?xml version="1.0" encoding="UTF-8"?>\n' +
      '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ' + total + ' ' + total + '" ' +
      'width="' + opts.size + '" height="' + opts.size + '" shape-rendering="crispEdges">\n' +
      '  <rect width="' + total + '" height="' + total + '" fill="' + opts.light + '"/>\n' +
      '  <path d="' + path + '" fill="' + opts.dark + '"/>\n' +
      '</svg>\n';
  }

  // ---------- UI state ----------

  function readOptions() {
    return {
      size: parseInt(el.size.value, 10),
      ecc: el.ecc.value,
      dark: el.dark.value,
      light: el.light.value,
      margin: parseInt(el.margin.value, 10)
    };
  }

  function setStatus(message, tone) {
    el.status.textContent = message || '';
    if (tone) {
      el.status.setAttribute('data-tone', tone);
    } else {
      el.status.removeAttribute('data-tone');
    }
  }

  function setError(message) {
    el.urlError.textContent = message || '';
    el.urlError.hidden = !message;
    if (message) {
      el.url.setAttribute('aria-invalid', 'true');
    } else {
      el.url.removeAttribute('aria-invalid');
    }
  }

  function setExportsEnabled(enabled) {
    [el.savePng, el.saveSvg, el.copy].forEach(function (button) {
      button.disabled = !enabled;
    });
  }

  function clearPreview() {
    current = null;
    el.canvas.hidden = true;
    el.meta.hidden = true;
    el.placeholder.hidden = false;
    setExportsEnabled(false);
  }

  /** Read the form, render the preview, and return true on success. */
  function render() {
    var text = normalize(el.url.value);

    if (!text) {
      clearPreview();
      setError('');
      setStatus('');
      return false;
    }

    var opts = readOptions();
    var qr;
    try {
      qr = encode(text, opts.ecc);
    } catch (err) {
      clearPreview();
      setError('That is too much data for one QR code. Try a shorter URL or a lower error-correction level.');
      return false;
    }

    setError('');
    draw(el.canvas, qr, opts);
    current = { text: text, qr: qr, opts: opts };

    el.canvas.hidden = false;
    el.placeholder.hidden = true;
    el.canvas.setAttribute('aria-label', 'QR code for ' + text);

    el.metaUrl.textContent = text;
    el.metaVersion.textContent = 'Level ' + opts.ecc + ' error correction';
    el.metaModules.textContent = qr.getModuleCount() + ' × ' + qr.getModuleCount() +
      ', plus a ' + opts.margin + '-module quiet zone';
    el.meta.hidden = false;

    setExportsEnabled(true);
    return true;
  }

  // ---------- Downloads ----------

  function download(blob, filename) {
    var url = URL.createObjectURL(blob);
    var link = document.createElement('a');
    link.href = url;
    link.download = filename;
    document.body.appendChild(link);
    link.click();
    link.remove();
    // Give the browser a moment to start the download before revoking.
    setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
  }

  function savePng() {
    if (!current) return;
    el.canvas.toBlob(function (blob) {
      if (!blob) {
        setStatus('Could not create the PNG. Try a smaller size.', 'error');
        return;
      }
      download(blob, filenameFor(current.text) + '.png');
      setStatus('Saved PNG at ' + el.canvas.width + ' × ' + el.canvas.height + '.');
    }, 'image/png');
  }

  function saveSvg() {
    if (!current) return;
    var svg = toSvg(current.qr, current.opts);
    download(new Blob([svg], { type: 'image/svg+xml' }), filenameFor(current.text) + '.svg');
    setStatus('Saved SVG - scales to any size without blurring.');
  }

  function copyImage() {
    if (!current) return;
    if (!navigator.clipboard || typeof window.ClipboardItem !== 'function') {
      setStatus('This browser cannot copy images. Use Save PNG instead.', 'error');
      return;
    }
    el.canvas.toBlob(function (blob) {
      if (!blob) {
        setStatus('Could not copy the image. Use Save PNG instead.', 'error');
        return;
      }
      navigator.clipboard.write([new ClipboardItem({ 'image/png': blob })]).then(
        function () { setStatus('Copied to clipboard - paste it anywhere.'); },
        function () { setStatus('Clipboard access was blocked. Use Save PNG instead.', 'error'); }
      );
    }, 'image/png');
  }

  // ---------- History ----------

  function loadHistory() {
    try {
      var parsed = JSON.parse(localStorage.getItem(HISTORY_KEY));
      return Array.isArray(parsed) ? parsed : [];
    } catch (err) {
      return [];
    }
  }

  function saveHistory(entries) {
    try {
      localStorage.setItem(HISTORY_KEY, JSON.stringify(entries));
    } catch (err) {
      // Storage may be full or blocked (private browsing). History is only a
      // convenience, so carry on without it.
    }
  }

  /** A small PNG data URL used as the history thumbnail. */
  function makeThumb(qr, opts) {
    var thumbCanvas = document.createElement('canvas');
    draw(thumbCanvas, qr, {
      size: THUMB_SIZE,
      margin: opts.margin,
      dark: opts.dark,
      light: opts.light
    });
    return thumbCanvas.toDataURL('image/png');
  }

  function renderHistory() {
    var entries = loadHistory();
    el.history.textContent = '';
    el.historyEmpty.hidden = entries.length > 0;
    el.clearHistory.hidden = entries.length === 0;

    entries.forEach(function (entry, index) {
      var button = document.createElement('button');
      button.type = 'button';
      button.className = 'history-item';
      button.dataset.index = String(index);
      button.title = entry.text;

      var img = document.createElement('img');
      img.src = entry.thumb;
      img.alt = '';

      var label = document.createElement('span');
      label.textContent = entry.text;

      button.appendChild(img);
      button.appendChild(label);

      var item = document.createElement('li');
      item.appendChild(button);
      el.history.appendChild(item);
    });
  }

  /** Add the current code to history, newest first, de-duplicated by text. */
  function remember() {
    if (!current) return;
    var text = current.text;
    var entries = loadHistory().filter(function (entry) {
      return entry.text !== text;
    });

    entries.unshift({
      text: text,
      size: current.opts.size,
      ecc: current.opts.ecc,
      dark: current.opts.dark,
      light: current.opts.light,
      margin: current.opts.margin,
      thumb: makeThumb(current.qr, current.opts),
      at: Date.now()
    });

    saveHistory(entries.slice(0, HISTORY_LIMIT));
    renderHistory();
  }

  function restore(index) {
    var entry = loadHistory()[index];
    if (!entry) return;

    el.url.value = entry.text;
    el.size.value = String(entry.size);
    el.ecc.value = entry.ecc;
    el.dark.value = entry.dark;
    el.light.value = entry.light;
    el.margin.value = String(entry.margin);
    el.marginOut.value = entry.margin;

    if (render()) {
      setStatus('Loaded from your recent codes.');
    }
  }

  // ---------- Wiring ----------

  el.form.addEventListener('submit', function (event) {
    event.preventDefault();
    if (!el.url.value.trim()) {
      setError('Enter a URL first.');
      el.url.focus();
      return;
    }
    if (render()) {
      remember();
      setStatus('Ready - save it as a PNG or SVG, or copy it straight to your clipboard.');
    }
  });

  el.form.addEventListener('reset', function () {
    // Let the browser clear the fields first, then sync the derived UI.
    setTimeout(function () {
      el.marginOut.value = el.margin.value;
      clearPreview();
      setError('');
      setStatus('');
      el.url.focus();
    }, 0);
  });

  // Live preview: re-render as the user types or adjusts an option.
  var debounceTimer;
  el.url.addEventListener('input', function () {
    clearTimeout(debounceTimer);
    debounceTimer = setTimeout(render, 200);
  });

  [el.size, el.ecc, el.dark, el.light, el.margin].forEach(function (input) {
    input.addEventListener('input', function () {
      el.marginOut.value = el.margin.value;
      render();
    });
  });

  el.savePng.addEventListener('click', savePng);
  el.saveSvg.addEventListener('click', saveSvg);
  el.copy.addEventListener('click', copyImage);

  el.clearHistory.addEventListener('click', function () {
    try {
      localStorage.removeItem(HISTORY_KEY);
    } catch (err) {
      /* Nothing to clear. */
    }
    renderHistory();
    setStatus('Cleared your recent codes.');
  });

  el.history.addEventListener('click', function (event) {
    var button = event.target.closest('.history-item');
    if (button) restore(parseInt(button.dataset.index, 10));
  });

  renderHistory();
  el.url.focus();
})();
