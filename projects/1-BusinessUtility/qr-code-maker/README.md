# QR Code Maker

A single-page web app that turns a URL into a QR code you can view and save as a PNG.

Built with vanilla HTML, CSS, and JavaScript — no build step, no framework, no server.

## Using it

Open [index.html](index.html) in a browser (double-click works — there is no build or install step), type
or paste a link, and press **Generate**. The code appears below the form; **Download PNG** saves it as
`qr-<domain>.png`.

The page needs an internet connection the first time you load it, because the QR library comes from a CDN
(see [Dependencies](#dependencies)).

If you prefer to serve it over HTTP rather than `file://`:

```sh
python -m http.server 8000
# then visit http://localhost:8000
```

## Files

| File | Purpose |
| --- | --- |
| [index.html](index.html) | Page markup and the CDN script tag |
| [styles.css](styles.css) | All styling, including light/dark themes |
| [app.js](app.js) | URL validation, QR rendering, PNG download |

## How it works

1. Whatever you type is normalized into a full URL — `example.com` becomes `https://example.com/`. Only
   `http` and `https` links are accepted, and anything unparseable produces an inline error message.
2. `QRCode.toCanvas()` draws the code onto a 1024 × 1024 canvas. That's larger than it appears on screen —
   CSS scales it down for display, so the saved PNG is print-quality rather than screen-quality.
3. **Download PNG** converts that same canvas to a `data:` URL and clicks a temporary `<a download>` link.
   Nothing is uploaded, and nothing is stored — the code is generated entirely in your browser.

Editing the URL field clears the displayed code, so what you see always matches what you last generated.

## Adjusting the defaults

The three rendering knobs are constants at the top of [app.js](app.js):

```js
const EXPORT_SIZE = 1024;       // pixel size of the rendered/downloaded PNG
const QUIET_ZONE = 2;           // blank margin around the code, in QR modules
const ERROR_CORRECTION = 'H';   // 'L' | 'M' | 'Q' | 'H'
```

`ERROR_CORRECTION` trades density for damage tolerance. `H` recovers from roughly 30% damage and leaves
room to drop a logo in the center; dropping to `M` produces a visibly less dense code for long URLs.

To change the code's colors, edit the `color` option in the same `QRCode.toCanvas()` call. Keep the
foreground much darker than the background or scanners will struggle — the code renders on a white
backing in both themes for the same reason.

## Dependencies

[node-qrcode](https://github.com/soldair/node-qrcode) 1.5.1, loaded from cdnjs and pinned with a
Subresource Integrity hash so a changed file won't execute:

```html
<script src="https://cdnjs.cloudflare.com/ajax/libs/qrcode/1.5.1/qrcode.min.js"
        integrity="sha384-kgapoJ184YmO0XnbSIH1J6dSp5rSYForqfjCgDat5yiSr8gjCnuTdRRCJXcVZ+pi"
        crossorigin="anonymous" referrerpolicy="no-referrer"></script>
```

If the library fails to load, generating shows an explanatory message instead of failing silently. To make
the page work fully offline, download that file next to `index.html` and point the `src` at the local copy
(dropping `integrity` and `crossorigin`).
