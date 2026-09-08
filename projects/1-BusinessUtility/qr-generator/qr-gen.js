#!/usr/bin/env node
'use strict';

const fs = require('node:fs');
const path = require('node:path');
const readline = require('node:readline/promises');
const QRCode = require('qrcode');

const OUTPUT_DIR = path.join(__dirname, 'output');
const FORMATS = ['png', 'svg', 'both'];
const ECC_LEVELS = ['L', 'M', 'Q', 'H'];

const USAGE = `Usage: node qr-gen.js [url] [options]

Creates a QR code for a URL and saves it in ./output.
Run without a url to be prompted for one.

Options:
  --name <base>     Base filename to use (default: derived from the url)
  --out <dir>       Directory to write into (default: ./output)
  --format <fmt>    png, svg, or both (default: both)
  --ecc <level>     Error correction: L, M, Q, or H (default: M)
  --size <px>       Width of the PNG in pixels (default: 512)
  --margin <mods>   Quiet zone width in modules (default: 2)
  -h, --help        Show this message
`;

function parseArgs(argv) {
  const options = {
    url: null,
    name: null,
    out: OUTPUT_DIR,
    format: 'both',
    ecc: 'M',
    size: 512,
    margin: 2,
  };

  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i];
    if (arg === '-h' || arg === '--help') return { help: true };

    if (arg.startsWith('--')) {
      const flag = arg.slice(2);
      const value = argv[i + 1];
      if (value === undefined) throw new Error(`Missing value for --${flag}`);
      i += 1;

      switch (flag) {
        case 'name': options.name = value; break;
        case 'out': options.out = path.resolve(value); break;
        case 'format': options.format = value.toLowerCase(); break;
        case 'ecc': options.ecc = value.toUpperCase(); break;
        case 'size': options.size = Number(value); break;
        case 'margin': options.margin = Number(value); break;
        default: throw new Error(`Unknown option --${flag}`);
      }
      continue;
    }

    if (options.url) throw new Error(`Unexpected extra argument: ${arg}`);
    options.url = arg;
  }

  if (!FORMATS.includes(options.format)) {
    throw new Error(`--format must be one of: ${FORMATS.join(', ')}`);
  }
  if (!ECC_LEVELS.includes(options.ecc)) {
    throw new Error(`--ecc must be one of: ${ECC_LEVELS.join(', ')}`);
  }
  if (!Number.isInteger(options.size) || options.size < 64 || options.size > 4096) {
    throw new Error('--size must be a whole number between 64 and 4096');
  }
  if (!Number.isInteger(options.margin) || options.margin < 0 || options.margin > 20) {
    throw new Error('--margin must be a whole number between 0 and 20');
  }

  return options;
}

/** Accepts anything with a scheme; bare inputs like "example.com" get https:// added. */
function normalizeUrl(input) {
  const trimmed = String(input).trim();
  if (!trimmed) throw new Error('No URL provided');

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

/** Turns a URL into a short, filesystem-safe basename, e.g. "leadingedje-com-careers". */
function slugify(url) {
  let source = url;
  try {
    const parsed = new URL(url);
    source = parsed.hostname + parsed.pathname;
  } catch {
    /* fall back to the raw string */
  }

  const slug = source
    .toLowerCase()
    .replace(/^www\./, '')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 60);

  return slug || 'qr-code';
}

/** Returns a path that does not exist yet, adding -2, -3, ... when needed. */
function uniquePath(dir, base, ext) {
  let candidate = path.join(dir, `${base}${ext}`);
  for (let n = 2; fs.existsSync(candidate); n += 1) {
    candidate = path.join(dir, `${base}-${n}${ext}`);
  }
  return candidate;
}

async function promptForUrl() {
  const rl = readline.createInterface({ input: process.stdin, output: process.stdout });
  try {
    return await rl.question('URL to encode: ');
  } finally {
    rl.close();
  }
}

async function generate(url, options) {
  fs.mkdirSync(options.out, { recursive: true });

  const base = options.name ? slugify(options.name) : slugify(url);
  const written = [];

  if (options.format === 'png' || options.format === 'both') {
    const file = uniquePath(options.out, base, '.png');
    await QRCode.toFile(file, url, {
      type: 'png',
      errorCorrectionLevel: options.ecc,
      width: options.size,
      margin: options.margin,
    });
    written.push(file);
  }

  if (options.format === 'svg' || options.format === 'both') {
    const file = uniquePath(options.out, base, '.svg');
    await QRCode.toFile(file, url, {
      type: 'svg',
      errorCorrectionLevel: options.ecc,
      width: options.size,
      margin: options.margin,
    });
    written.push(file);
  }

  return written;
}

async function main() {
  const options = parseArgs(process.argv.slice(2));
  if (options.help) {
    process.stdout.write(USAGE);
    return;
  }

  const url = normalizeUrl(options.url ?? (await promptForUrl()));
  const written = await generate(url, options);

  console.log(`\nQR code for ${url}`);
  console.log(await QRCode.toString(url, { type: 'terminal', small: true }));
  for (const file of written) {
    console.log(`Saved ${path.relative(process.cwd(), file)}`);
  }
}

if (require.main === module) {
  main().catch((error) => {
    console.error(`Error: ${error.message}`);
    process.exitCode = 1;
  });
}

module.exports = { parseArgs, normalizeUrl, slugify, generate };
