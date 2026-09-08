/*
 * app.js — DOM wiring for the QR generator.
 *
 * All the QR logic lives in qr-encoder.js. This file only reads the controls,
 * paints the result to a canvas, and handles the download.
 */

(function () {
  'use strict';

  const { generateQR } = window.QREncoder;

  const el = {
    text: document.getElementById('text'),
    ec: document.getElementById('ec'),
    size: document.getElementById('size'),
    sizeValue: document.getElementById('sizeValue'),
    fg: document.getElementById('fg'),
    bg: document.getElementById('bg'),
    swap: document.getElementById('swap'),
    canvas: document.getElementById('canvas'),
    placeholder: document.getElementById('placeholder'),
    error: document.getElementById('error'),
    meta: document.getElementById('meta'),
    warning: document.getElementById('warning'),
    download: document.getElementById('download'),
    reset: document.getElementById('reset'),
  };

  const DEFAULTS = {
    text: 'https://example.com',
    ec: 'M',
    size: '320',
    fg: '#000000',
    bg: '#ffffff',
  };

  // Four light modules of margin on every side. Scanners need this to find the
  // symbol; a QR code rendered flush to its edge often will not read at all.
  const QUIET_ZONE = 4;

  let lastRender = null; // remembers whether there's something to download

  // ------------------------------------------------------------- rendering

  function render() {
    const text = el.text.value.trim();
    const ecLevel = el.ec.value;
    const targetPx = Number(el.size.value);

    el.sizeValue.textContent = `${targetPx}px`;

    if (text === '') {
      showPlaceholder();
      return;
    }

    let result;
    try {
      result = generateQR(text, { ecLevel });
    } catch (err) {
      showError(err.message);
      return;
    }

    const total = result.size + QUIET_ZONE * 2;
    // Round to a whole number of pixels per module so every module is the same
    // width. Fractional scaling is what makes rendered codes look fuzzy.
    const scale = Math.max(1, Math.round(targetPx / total));
    const px = total * scale;

    const canvas = el.canvas;
    canvas.width = px;
    canvas.height = px;
    canvas.style.width = `${px}px`;

    const ctx = canvas.getContext('2d');
    ctx.fillStyle = el.bg.value;
    ctx.fillRect(0, 0, px, px);
    ctx.fillStyle = el.fg.value;
    for (let row = 0; row < result.size; row++) {
      for (let col = 0; col < result.size; col++) {
        if (result.modules[row][col]) {
          ctx.fillRect((col + QUIET_ZONE) * scale, (row + QUIET_ZONE) * scale, scale, scale);
        }
      }
    }

    lastRender = result;
    el.canvas.hidden = false;
    el.placeholder.hidden = true;
    el.error.hidden = true;
    el.download.disabled = false;

    el.meta.textContent =
      `Version ${result.version} · ${result.size}×${result.size} modules · ` +
      `level ${result.ecLevel} · mask ${result.maskId} · ${result.byteLength} bytes · ${px}px`;

    checkContrast();
  }

  function showPlaceholder() {
    el.canvas.width = 0;
    el.canvas.height = 0;
    el.canvas.hidden = true;
    el.placeholder.hidden = false;
    el.error.hidden = true;
    el.warning.hidden = true;
    el.meta.textContent = '';
    el.download.disabled = true;
    lastRender = null;
  }

  function showError(message) {
    el.canvas.width = 0;
    el.canvas.height = 0;
    el.canvas.hidden = true;
    el.placeholder.hidden = true;
    el.error.hidden = false;
    el.error.textContent = message;
    el.warning.hidden = true;
    el.meta.textContent = '';
    el.download.disabled = true;
    lastRender = null;
  }

  // -------------------------------------------------------------- contrast
  //
  // A code can be perfectly valid and still unscannable if the colors are too
  // close together, or inverted. Warn rather than silently produce something
  // that won't read.

  function relativeLuminance(hex) {
    const channels = [1, 3, 5].map((i) => {
      const c = parseInt(hex.slice(i, i + 2), 16) / 255;
      return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
    });
    return 0.2126 * channels[0] + 0.7152 * channels[1] + 0.0722 * channels[2];
  }

  function checkContrast() {
    const fg = relativeLuminance(el.fg.value);
    const bg = relativeLuminance(el.bg.value);
    const ratio = (Math.max(fg, bg) + 0.05) / (Math.min(fg, bg) + 0.05);

    let message = null;
    if (ratio < 3) {
      message = `Low contrast (${ratio.toFixed(1)}:1). Most scanners need a strong light/dark difference — this may not read.`;
    } else if (fg > bg) {
      message =
        'Inverted colors: the code is light on dark. Many scanners only accept dark modules on a light background.';
    }

    el.warning.hidden = message === null;
    if (message) el.warning.textContent = message;
  }

  // -------------------------------------------------------------- download

  function download() {
    if (!lastRender) return;
    const link = document.createElement('a');
    link.download = filename();
    // The canvas only ever gets fillRect calls, so it is never tainted and
    // this works from file:// as well as over http.
    link.href = el.canvas.toDataURL('image/png');
    link.click();
  }

  /** Name the file after the URL's host when we can recognise one. */
  function filename() {
    const raw = el.text.value.trim();
    let stem = 'qrcode';
    try {
      const url = new URL(raw.includes('://') ? raw : `https://${raw}`);
      if (url.hostname) stem = `qrcode-${url.hostname.replace(/^www\./, '')}`;
    } catch (e) {
      /* not a URL — keep the generic name */
    }
    return `${stem.replace(/[^a-zA-Z0-9.-]/g, '-')}.png`;
  }

  // ---------------------------------------------------------------- events

  /** Coalesce keystrokes so we re-encode once the typing pauses. */
  function debounce(fn, ms) {
    let timer = null;
    return function () {
      clearTimeout(timer);
      timer = setTimeout(fn, ms);
    };
  }

  const renderSoon = debounce(render, 150);

  el.text.addEventListener('input', renderSoon);
  // The rest are discrete choices, so there's nothing to wait for.
  el.ec.addEventListener('change', render);
  el.size.addEventListener('input', render);
  el.fg.addEventListener('input', render);
  el.bg.addEventListener('input', render);

  el.swap.addEventListener('click', function () {
    const fg = el.fg.value;
    el.fg.value = el.bg.value;
    el.bg.value = fg;
    render();
  });

  el.download.addEventListener('click', download);

  el.reset.addEventListener('click', function () {
    el.text.value = DEFAULTS.text;
    el.ec.value = DEFAULTS.ec;
    el.size.value = DEFAULTS.size;
    el.fg.value = DEFAULTS.fg;
    el.bg.value = DEFAULTS.bg;
    render();
  });

  render();
})();
