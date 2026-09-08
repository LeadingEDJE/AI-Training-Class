'use strict';

const QRCode = require('qrcode');

const ECC_LEVELS = ['L', 'M', 'Q', 'H'];
const MIN_SIZE = 128;
const MAX_SIZE = 2048;

/** Accepts anything with a scheme; bare inputs like "example.com" get https:// added. */
function normalizeUrl(input) {
  const trimmed = String(input ?? '').trim();
  if (!trimmed) throw new Error('Enter a URL first');

  const candidate = /^[a-z][a-z0-9+.-]*:/i.test(trimmed) ? trimmed : `https://${trimmed}`;
  let parsed;
  try {
    parsed = new URL(candidate);
  } catch {
    throw new Error(`That does not look like a URL: ${trimmed}`);
  }
  if ((parsed.protocol === 'http:' || parsed.protocol === 'https:') && !parsed.hostname) {
    throw new Error(`That does not look like a URL: ${trimmed}`);
  }
  return parsed.toString();
}

/** Turns text into a short, filesystem-safe basename, e.g. "leadingedje-com-careers". */
function slugify(text) {
  let source = String(text ?? '');
  try {
    const parsed = new URL(source);
    source = parsed.hostname + parsed.pathname;
  } catch {
    /* not a URL - slug the raw string */
  }

  const slug = source
    .toLowerCase()
    .replace(/^www\./, '')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 60);

  return slug || 'qr-code';
}

/** Clamps and validates the render options that come in from the browser. */
function normalizeOptions({ ecc, size } = {}) {
  const level = String(ecc ?? 'M').toUpperCase();
  if (!ECC_LEVELS.includes(level)) {
    throw new Error(`Error correction must be one of: ${ECC_LEVELS.join(', ')}`);
  }

  const width = Number(size ?? 512);
  if (!Number.isInteger(width) || width < MIN_SIZE || width > MAX_SIZE) {
    throw new Error(`Size must be a whole number between ${MIN_SIZE} and ${MAX_SIZE}`);
  }

  return { errorCorrectionLevel: level, width, margin: 2 };
}

/** Renders a QR code as an inline SVG string, ready to drop into the page. */
function renderSvg(url, options) {
  return QRCode.toString(url, { ...normalizeOptions(options), type: 'svg' });
}

/** Renders a QR code as PNG bytes. */
function renderPng(url, options) {
  return QRCode.toBuffer(url, { ...normalizeOptions(options), type: 'png' });
}

module.exports = { ECC_LEVELS, MIN_SIZE, MAX_SIZE, normalizeUrl, slugify, normalizeOptions, renderSvg, renderPng };
