'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { after, test } = require('node:test');
const { PNG } = require('pngjs');
const jsQR = require('jsqr');

const { normalizeUrl, slugify, normalizeOptions, renderPng, renderSvg } = require('../lib/qr');
const library = require('../lib/library');
const { server } = require('../server');

/** Decodes a PNG buffer back into the text it encodes. */
function decode(pngBuffer) {
  const png = PNG.sync.read(pngBuffer);
  const result = jsQR(Uint8ClampedArray.from(png.data), png.width, png.height);
  return result?.data ?? null;
}

const saved = [];

after(() => {
  for (const id of saved) library.remove(id);
  if (server.listening) server.close();
});

test('normalizeUrl adds https to bare hosts and keeps other schemes', () => {
  assert.equal(normalizeUrl('leadingedje.com'), 'https://leadingedje.com/');
  assert.equal(normalizeUrl('  https://example.com/a?b=1 '), 'https://example.com/a?b=1');
  assert.equal(normalizeUrl('mailto:hi@example.com'), 'mailto:hi@example.com');
});

test('normalizeUrl rejects empty and malformed input', () => {
  assert.throws(() => normalizeUrl('   '), /Enter a URL/);
  assert.throws(() => normalizeUrl('https://'), /does not look like a URL/);
});

test('slugify produces a short filesystem-safe name', () => {
  assert.equal(slugify('https://www.leadingedje.com/careers'), 'leadingedje-com-careers');
  assert.equal(slugify('Careers Flyer!'), 'careers-flyer');
  assert.equal(slugify('///'), 'qr-code');
});

test('normalizeOptions validates size and error correction', () => {
  assert.deepEqual(normalizeOptions({}), { errorCorrectionLevel: 'M', width: 512, margin: 2 });
  assert.equal(normalizeOptions({ ecc: 'h' }).errorCorrectionLevel, 'H');
  assert.throws(() => normalizeOptions({ ecc: 'X' }), /Error correction/);
  assert.throws(() => normalizeOptions({ size: 12 }), /Size must be/);
  assert.throws(() => normalizeOptions({ size: 512.5 }), /Size must be/);
});

test('renderSvg returns an svg sized as requested', async () => {
  const svg = await renderSvg('https://example.com', { size: 256 });
  assert.match(svg, /^<svg /);
  assert.match(svg, /width="256"/);
});

test('a rendered PNG scans back to the original URL', async () => {
  const url = 'https://www.leadingedje.com/careers';
  assert.equal(decode(await renderPng(url, { size: 512 })), url);
});

test('saving writes both files and lists them newest first', async () => {
  const first = await library.save({ url: 'https://example.com/one', label: 'test one', size: 256 });
  saved.push(first.id);
  const second = await library.save({ url: 'https://example.com/two', label: 'test two', size: 256 });
  saved.push(second.id);

  assert.equal(first.id, 'test-one');
  for (const file of Object.values(first.files)) {
    assert.ok(fs.existsSync(path.join(library.LIBRARY_DIR, file)), `${file} should exist`);
  }

  const ids = library.list().map((entry) => entry.id);
  assert.ok(ids.indexOf(second.id) < ids.indexOf(first.id), 'newest entry comes first');
});

test('saving the same name twice does not overwrite the first file', async () => {
  const first = await library.save({ url: 'https://example.com/dup', label: 'dup test', size: 256 });
  const second = await library.save({ url: 'https://example.com/dup', label: 'dup test', size: 256 });
  saved.push(first.id, second.id);

  assert.equal(first.id, 'dup-test');
  assert.equal(second.id, 'dup-test-2');
});

test('remove deletes the files and the index entry', async () => {
  const entry = await library.save({ url: 'https://example.com/gone', label: 'gone test', size: 256 });
  assert.equal(library.remove(entry.id), true);
  assert.equal(fs.existsSync(path.join(library.LIBRARY_DIR, entry.files.png)), false);
  assert.equal(library.list().some((item) => item.id === entry.id), false);
  assert.equal(library.remove(entry.id), false);
});

test('resolveFile refuses paths outside the library', () => {
  assert.equal(library.resolveFile('../server.js'), null);
  assert.equal(library.resolveFile('nope.txt'), null);
});

test('the server previews, downloads, saves, and serves library files', async () => {
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  const base = `http://127.0.0.1:${server.address().port}`;

  const page = await fetch(`${base}/`);
  assert.equal(page.status, 200);
  assert.match(await page.text(), /QR Studio/);

  const previewResponse = await fetch(`${base}/api/qr?url=leadingedje.com&size=256`);
  const preview = await previewResponse.json();
  assert.equal(preview.url, 'https://leadingedje.com/');
  assert.match(preview.svg, /^<svg /);

  const bad = await fetch(`${base}/api/qr?url=`);
  assert.equal(bad.status, 400);
  assert.match((await bad.json()).error, /Enter a URL/);

  const download = await fetch(`${base}/api/download?url=example.com&format=png&size=256&label=via api`);
  assert.equal(download.headers.get('content-disposition'), 'attachment; filename="via-api.png"');
  assert.equal(decode(Buffer.from(await download.arrayBuffer())), 'https://example.com/');

  const saveResponse = await fetch(`${base}/api/library`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ url: 'example.com/server', label: 'server test', size: 256 }),
  });
  assert.equal(saveResponse.status, 201);
  const entry = await saveResponse.json();
  saved.push(entry.id);

  const file = await fetch(`${base}/library/${entry.files.png}`);
  assert.equal(file.headers.get('content-type'), 'image/png');
  assert.equal(decode(Buffer.from(await file.arrayBuffer())), 'https://example.com/server');

  const listed = await (await fetch(`${base}/api/library`)).json();
  assert.ok(listed.some((item) => item.id === entry.id));

  const missing = await fetch(`${base}/api/library/not-a-real-id`, { method: 'DELETE' });
  assert.equal(missing.status, 404);

  const traversal = await fetch(`${base}/library/..%2Fserver.js`);
  assert.equal(traversal.status, 404);
});
