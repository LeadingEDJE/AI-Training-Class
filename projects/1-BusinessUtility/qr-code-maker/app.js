/**
 * QR Code Maker
 *
 * Takes a URL, renders it as a QR code on a canvas, and lets you save it as a PNG.
 * Rendering is handled by node-qrcode (loaded in index.html as the global `QRCode`).
 */
(() => {
  'use strict';

  // Rendering defaults. Change these to adjust every code the page produces.
  const EXPORT_SIZE = 1024;         // width/height in pixels of the canvas we render and download
  const QUIET_ZONE = 2;             // blank margin around the code, measured in QR modules
  const ERROR_CORRECTION = 'H';     // 'L' | 'M' | 'Q' | 'H' — H survives the most damage

  const form = document.getElementById('qr-form');
  const input = document.getElementById('url');
  const message = document.getElementById('message');
  const result = document.getElementById('result');
  const canvas = document.getElementById('qr-canvas');
  const encodedLink = document.getElementById('encoded-link');
  const downloadButton = document.getElementById('download');

  // The URL currently drawn on the canvas, used to name the downloaded file.
  let currentUrl = null;

  /**
   * Turns whatever the user typed into a full http(s) URL.
   * Throws an Error with a user-facing message when the input can't be used.
   */
  function normalizeUrl(raw) {
    const trimmed = raw.trim();
    if (!trimmed) {
      throw new Error('Enter a link to generate a code.');
    }

    // Assume https:// when no scheme was typed, so "example.com" works.
    const hasScheme = /^[a-z][a-z0-9+.-]*:\/\//i.test(trimmed);
    let parsed;
    try {
      parsed = new URL(hasScheme ? trimmed : `https://${trimmed}`);
    } catch {
      throw new Error(`"${trimmed}" doesn't look like a valid link.`);
    }

    if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
      throw new Error('Only http and https links are supported.');
    }
    if (!parsed.hostname.includes('.') && parsed.hostname !== 'localhost') {
      throw new Error(`"${trimmed}" doesn't look like a complete link — check the domain name.`);
    }

    return parsed.href;
  }

  /** Builds a readable file name from the URL, e.g. qr-leadingedje.com.png */
  function fileNameFor(url) {
    const host = new URL(url).hostname.replace(/^www\./, '');
    return `qr-${host.replace(/[^a-z0-9.-]+/gi, '-')}.png`;
  }

  function showMessage(text, isError = false) {
    message.textContent = text;
    message.classList.toggle('message--error', isError);
  }

  function showResult(url) {
    currentUrl = url;
    encodedLink.textContent = url;
    encodedLink.href = url;
    result.hidden = false;
  }

  function hideResult() {
    currentUrl = null;
    result.hidden = true;
  }

  async function generate(rawInput) {
    const url = normalizeUrl(rawInput);

    await QRCode.toCanvas(canvas, url, {
      width: EXPORT_SIZE,
      margin: QUIET_ZONE,
      errorCorrectionLevel: ERROR_CORRECTION,
      color: { dark: '#000000', light: '#ffffff' }
    });

    // node-qrcode sets an inline 1024px size; drop it so CSS controls the displayed size.
    canvas.style.removeProperty('width');
    canvas.style.removeProperty('height');

    showResult(url);
  }

  form.addEventListener('submit', async (event) => {
    event.preventDefault();

    if (typeof QRCode === 'undefined') {
      hideResult();
      showMessage('The QR library failed to load. Check your internet connection and reload.', true);
      return;
    }

    try {
      await generate(input.value);
      showMessage('Code ready — download it below.');
    } catch (error) {
      hideResult();
      showMessage(error.message, true);
    }
  });

  downloadButton.addEventListener('click', () => {
    if (!currentUrl) return;

    const link = document.createElement('a');
    link.href = canvas.toDataURL('image/png');
    link.download = fileNameFor(currentUrl);
    link.click();
  });

  // A fresh edit means the code on screen no longer matches the input.
  input.addEventListener('input', () => {
    hideResult();
    showMessage('');
  });
})();
