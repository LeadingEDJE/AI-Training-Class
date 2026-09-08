# QR Studio

A small local web app for the everyday job of "I need a QR code for this link."
Paste a URL, see the code immediately, then download it or keep it in your
library folder so you can grab it again later.

Built for **Prompt 2** of [Project 1](../Project1.md) — generate, **view, and
save** QR codes for URLs.

## Setup

Requires [Node.js](https://nodejs.org/) 18 or newer.

```bash
cd projects/1-BusinessUtility/qr-studio
npm install
npm start
```

Then open <http://localhost:4173>. Set a different port with `PORT=5000 npm start`.
The server only listens on `127.0.0.1`, so it is not reachable from other machines.

## Using it

1. Type or paste a URL. The preview updates as you type — bare hosts like
   `example.com` get `https://` added, and `mailto:`/`tel:` links work too.
2. Optionally set a **Name** (used for the filename), a **Size**, and an
   **Error correction** level.
3. Then either:
   - **Download PNG / SVG** — a normal browser download, for sending to someone now.
   - **Save to library** — writes both a `.png` and an `.svg` into
     [library/](library/) and adds it to the gallery at the bottom of the page.

The library gallery shows every code you have saved, with its URL and settings,
plus per-item download and delete buttons. Names are never overwritten: saving
`careers-flyer` twice gives you `careers-flyer` and `careers-flyer-2`.

### Choosing settings

| Setting | Guidance |
| --- | --- |
| Size | 512 px for screens and email, 1024 px+ for print. The SVG scales to any size. |
| Error correction | `M` is a good default. `H` keeps scanning when the code is smudged or partly covered (e.g. a logo on top), at the cost of a denser code. |

## Tests

```bash
npm test
```

Covers URL normalization, filename slugging, option validation, the library
save/list/delete cycle, and the HTTP endpoints. The image tests decode the
generated PNGs with a real QR reader to confirm they scan back to the original
URL, and check that `/library/` refuses path traversal.

## How it works

Rendering happens on the server, so there is one encoding path and no
client-side QR library to keep up to date.

| File | Role |
| --- | --- |
| [server.js](server.js) | Static files plus a small JSON API (`/api/qr`, `/api/download`, `/api/library`) |
| [lib/qr.js](lib/qr.js) | URL normalization, filename slugging, option validation, PNG/SVG rendering |
| [lib/library.js](lib/library.js) | Reading and writing `library/` and its `library.json` index |
| [public/](public/) | The page: vanilla HTML, CSS, and JavaScript — no build step |

`library/library.json` is the index the gallery reads. Deleting image files by
hand is safe — entries with missing files are dropped on the next load. The
folder's contents are git-ignored, so your saved codes stay local.

Encoding is handled by the [`qrcode`](https://www.npmjs.com/package/qrcode)
package; the tests use [`jsqr`](https://www.npmjs.com/package/jsqr) and
[`pngjs`](https://www.npmjs.com/package/pngjs) to decode.
