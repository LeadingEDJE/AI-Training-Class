'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { test } = require('node:test');
const { PNG } = require('pngjs');
const jsQR = require('jsqr').default;

const { parseArgs, normalizeUrl, slugify, generate } = require('../qr-gen.js');

function tempDir() {
  return fs.mkdtempSync(path.join(os.tmpdir(), 'qr-gen-test-'));
}

/** Decodes a generated PNG back to the string it encodes. */
function decodePng(file) {
  const png = PNG.sync.read(fs.readFileSync(file));
  const result = jsQR(new Uint8ClampedArray(png.data), png.width, png.height);
  assert.ok(result, `could not decode ${file}`);
  return result.data;
}

test('normalizeUrl keeps full URLs and adds https:// to bare hosts', () => {
  assert.equal(normalizeUrl('https://www.leadingedje.com/careers'), 'https://www.leadingedje.com/careers');
  assert.equal(normalizeUrl('leadingedje.com'), 'https://leadingedje.com/');
  assert.equal(normalizeUrl('  https://example.com  '), 'https://example.com/');
  assert.equal(normalizeUrl('mailto:hi@example.com'), 'mailto:hi@example.com');
});

test('normalizeUrl rejects input that is not a URL', () => {
  assert.throws(() => normalizeUrl(''), /No URL provided/);
  assert.throws(() => normalizeUrl('https://'), /does not look like a URL/);
});

test('slugify produces a readable, safe basename', () => {
  assert.equal(slugify('https://www.leadingedje.com/careers'), 'leadingedje-com-careers');
  assert.equal(slugify('https://example.com/'), 'example-com');
  assert.equal(slugify('!!!'), 'qr-code');
});

test('parseArgs reads the url and options', () => {
  const options = parseArgs(['https://example.com', '--format', 'png', '--ecc', 'h', '--size', '256']);
  assert.equal(options.url, 'https://example.com');
  assert.equal(options.format, 'png');
  assert.equal(options.ecc, 'H');
  assert.equal(options.size, 256);
});

test('parseArgs rejects bad input', () => {
  assert.throws(() => parseArgs(['--format', 'gif']), /--format must be one of/);
  assert.throws(() => parseArgs(['--ecc', 'Z']), /--ecc must be one of/);
  assert.throws(() => parseArgs(['--size', '10']), /--size must be/);
  assert.throws(() => parseArgs(['--nope', '1']), /Unknown option/);
  assert.throws(() => parseArgs(['--name']), /Missing value/);
  assert.throws(() => parseArgs(['a', 'b']), /Unexpected extra argument/);
});

test('generate writes a png and an svg that decode back to the url', async () => {
  const out = tempDir();
  const url = 'https://www.leadingedje.com/careers?utm_source=workshop';

  const written = await generate(url, { out, format: 'both', ecc: 'M', size: 512, margin: 2, name: null });

  assert.deepEqual(written.map((f) => path.basename(f)), [
    'leadingedje-com-careers.png',
    'leadingedje-com-careers.svg',
  ]);
  assert.equal(decodePng(written[0]), url);
  assert.match(fs.readFileSync(written[1], 'utf8'), /^<\?xml/);
});

test('generate does not overwrite existing files', async () => {
  const out = tempDir();
  const url = 'https://example.com';
  const options = { out, format: 'png', ecc: 'M', size: 256, margin: 2, name: null };

  const first = await generate(url, options);
  const second = await generate(url, options);

  assert.equal(path.basename(first[0]), 'example-com.png');
  assert.equal(path.basename(second[0]), 'example-com-2.png');
  assert.equal(decodePng(second[0]), url);
});

test('generate honors --name and every error correction level', async () => {
  const out = tempDir();
  const url = 'https://example.com/a/fairly/long/path?with=query&and=more';

  for (const ecc of ['L', 'M', 'Q', 'H']) {
    const written = await generate(url, { out, format: 'png', ecc, size: 512, margin: 2, name: `level ${ecc}` });
    assert.equal(path.basename(written[0]), `level-${ecc.toLowerCase()}.png`);
    assert.equal(decodePng(written[0]), url);
  }
});
