#!/usr/bin/env node
'use strict';

const fs = require('node:fs');
const http = require('node:http');
const path = require('node:path');

const library = require('./lib/library');
const { normalizeUrl, renderPng, renderSvg, slugify } = require('./lib/qr');

const PUBLIC_DIR = path.join(__dirname, 'public');
const PORT = Number(process.env.PORT ?? 4173);

const CONTENT_TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.png': 'image/png',
  '.svg': 'image/svg+xml',
};

function sendJson(res, status, body) {
  const payload = JSON.stringify(body);
  res.writeHead(status, { 'content-type': 'application/json; charset=utf-8' });
  res.end(payload);
}

async function readJsonBody(req) {
  const chunks = [];
  let bytes = 0;
  for await (const chunk of req) {
    bytes += chunk.length;
    if (bytes > 64 * 1024) throw new Error('Request body is too large');
    chunks.push(chunk);
  }
  if (!chunks.length) return {};
  return JSON.parse(Buffer.concat(chunks).toString('utf8'));
}

/** Serves a file from public/, or 404s. Only the listed extensions are allowed. */
function serveStatic(pathname, res) {
  const relative = pathname === '/' ? 'index.html' : pathname.slice(1);
  const target = path.join(PUBLIC_DIR, relative);
  const type = CONTENT_TYPES[path.extname(target).toLowerCase()];

  if (!type || !target.startsWith(PUBLIC_DIR) || !fs.existsSync(target)) {
    res.writeHead(404, { 'content-type': 'text/plain; charset=utf-8' });
    res.end('Not found');
    return;
  }

  res.writeHead(200, { 'content-type': type, 'cache-control': 'no-store' });
  fs.createReadStream(target).pipe(res);
}

/** GET /api/qr?url=...&ecc=M&size=512 - inline SVG plus a suggested filename. */
async function handlePreview(query, res) {
  const url = normalizeUrl(query.get('url'));
  const options = { ecc: query.get('ecc'), size: Number(query.get('size')) };

  sendJson(res, 200, {
    url,
    slug: slugify(url),
    svg: await renderSvg(url, options),
  });
}

/** GET /api/download?url=...&format=png|svg - a file the browser can save. */
async function handleDownload(query, res) {
  const url = normalizeUrl(query.get('url'));
  const format = query.get('format') === 'svg' ? 'svg' : 'png';
  const options = { ecc: query.get('ecc'), size: Number(query.get('size')) };
  const label = query.get('label');
  const base = slugify(label?.trim() ? label : url);

  const body = format === 'svg'
    ? Buffer.from(await renderSvg(url, options), 'utf8')
    : await renderPng(url, options);

  res.writeHead(200, {
    'content-type': CONTENT_TYPES[`.${format}`],
    'content-disposition': `attachment; filename="${base}.${format}"`,
    'content-length': body.length,
  });
  res.end(body);
}

/** POST /api/library - render and store a PNG + SVG in library/. */
async function handleSave(req, res) {
  const body = await readJsonBody(req);
  const entry = await library.save({
    url: normalizeUrl(body.url),
    label: body.label,
    ecc: body.ecc,
    size: Number(body.size),
  });
  sendJson(res, 201, entry);
}

function serveLibraryFile(name, res) {
  const target = library.resolveFile(name);
  if (!target) {
    sendJson(res, 404, { error: 'That file is not in the library' });
    return;
  }
  res.writeHead(200, {
    'content-type': CONTENT_TYPES[path.extname(target).toLowerCase()],
    'cache-control': 'no-store',
  });
  fs.createReadStream(target).pipe(res);
}

async function route(req, res) {
  const { pathname, searchParams } = new URL(req.url, `http://localhost:${PORT}`);

  if (req.method === 'GET' && pathname === '/api/qr') return handlePreview(searchParams, res);
  if (req.method === 'GET' && pathname === '/api/download') return handleDownload(searchParams, res);
  if (req.method === 'GET' && pathname === '/api/library') return sendJson(res, 200, library.list());
  if (req.method === 'POST' && pathname === '/api/library') return handleSave(req, res);

  if (req.method === 'DELETE' && pathname.startsWith('/api/library/')) {
    const id = decodeURIComponent(pathname.slice('/api/library/'.length));
    return library.remove(id)
      ? sendJson(res, 200, { id })
      : sendJson(res, 404, { error: 'No saved code with that id' });
  }

  if (req.method === 'GET' && pathname.startsWith('/library/')) {
    return serveLibraryFile(decodeURIComponent(pathname.slice('/library/'.length)), res);
  }

  if (req.method === 'GET') return serveStatic(pathname, res);

  sendJson(res, 405, { error: `${req.method} is not supported here` });
}

const server = http.createServer((req, res) => {
  Promise.resolve()
    .then(() => route(req, res))
    .catch((error) => {
      if (!res.headersSent) sendJson(res, 400, { error: error.message });
      else res.end();
    });
});

if (require.main === module) {
  server.listen(PORT, '127.0.0.1', () => {
    console.log(`QR Studio is running at http://localhost:${PORT}`);
    console.log(`Saved codes go to ${path.relative(process.cwd(), library.LIBRARY_DIR)}`);
    console.log('Press Ctrl+C to stop.');
  });
}

module.exports = { server, PORT };
