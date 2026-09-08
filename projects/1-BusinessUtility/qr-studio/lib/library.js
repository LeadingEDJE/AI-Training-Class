'use strict';

const fs = require('node:fs');
const path = require('node:path');

const { slugify, renderPng, renderSvg, normalizeOptions } = require('./qr');

const LIBRARY_DIR = path.join(__dirname, '..', 'library');
const INDEX_FILE = path.join(LIBRARY_DIR, 'library.json');

function readIndex() {
  try {
    const entries = JSON.parse(fs.readFileSync(INDEX_FILE, 'utf8'));
    return Array.isArray(entries) ? entries : [];
  } catch {
    return [];
  }
}

function writeIndex(entries) {
  fs.mkdirSync(LIBRARY_DIR, { recursive: true });
  fs.writeFileSync(INDEX_FILE, `${JSON.stringify(entries, null, 2)}\n`);
}

/** Saved codes, newest first, skipping any whose files were deleted by hand. */
function list() {
  const entries = readIndex();
  const present = entries.filter((entry) =>
    Object.values(entry.files ?? {}).every((file) => fs.existsSync(path.join(LIBRARY_DIR, file))));

  if (present.length !== entries.length) writeIndex(present);
  return [...present].sort((a, b) => String(b.savedAt).localeCompare(String(a.savedAt)));
}

/** Returns a basename not already used in the library, adding -2, -3, ... when needed. */
function uniqueBase(base) {
  let candidate = base;
  for (let n = 2; fs.existsSync(path.join(LIBRARY_DIR, `${candidate}.png`)); n += 1) {
    candidate = `${base}-${n}`;
  }
  return candidate;
}

/** Writes a PNG and an SVG into library/ and records them in the index. */
async function save({ url, label, ecc, size }) {
  const options = normalizeOptions({ ecc, size });
  fs.mkdirSync(LIBRARY_DIR, { recursive: true });

  const base = uniqueBase(slugify(label?.trim() ? label : url));
  const files = { png: `${base}.png`, svg: `${base}.svg` };

  fs.writeFileSync(path.join(LIBRARY_DIR, files.png), await renderPng(url, { ecc, size }));
  fs.writeFileSync(path.join(LIBRARY_DIR, files.svg), await renderSvg(url, { ecc, size }));

  const entry = {
    id: base,
    url,
    label: label?.trim() || '',
    ecc: options.errorCorrectionLevel,
    size: options.width,
    savedAt: new Date().toISOString(),
    files,
  };

  writeIndex([...readIndex(), entry]);
  return entry;
}

/** Deletes an entry and its files. Returns false when the id is unknown. */
function remove(id) {
  const entries = readIndex();
  const entry = entries.find((candidate) => candidate.id === id);
  if (!entry) return false;

  for (const file of Object.values(entry.files ?? {})) {
    fs.rmSync(path.join(LIBRARY_DIR, file), { force: true });
  }
  writeIndex(entries.filter((candidate) => candidate.id !== id));
  return true;
}

/** Resolves a library filename to an absolute path, refusing anything outside library/. */
function resolveFile(name) {
  if (!/^[a-z0-9-]+\.(png|svg)$/i.test(name)) return null;
  const target = path.join(LIBRARY_DIR, name);
  return fs.existsSync(target) ? target : null;
}

module.exports = { LIBRARY_DIR, list, save, remove, resolveFile };
