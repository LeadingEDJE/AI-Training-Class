# QR Code Generator

A single-page QR code generator: type a URL, see the code instantly, then save it
as a PNG or SVG or copy it straight to your clipboard.

Built with vanilla HTML, CSS, and JavaScript — no build step, no framework, no
server. The only dependency is a QR encoding library vendored into `vendor/`.

## Running it

Open `index.html` in a browser. That's it.

Everything works from the local file system, including saving images. If you'd
rather serve it (handy if your browser restricts clipboard access on `file://`):

```bash
python -m http.server 8000
# then visit http://localhost:8000
```

## What it does

- **Live preview** — the code redraws as you type and as you change options.
- **Friendly URLs** — `example.com/page` becomes `https://example.com/page`
  automatically. Other schemes (`mailto:`, `tel:`) and plain text pass through
  untouched.
- **Save PNG** at 256, 512, 1024, or 2048 px — small for web, large for print.
- **Save SVG** for a vector version that scales to any size without blurring.
- **Copy image** puts the PNG on your clipboard to paste into slides or chat.
- **Recent codes** keeps your last 12 in this browser. Click one to load it back
  with its settings.

### Options

| Option | Notes |
| --- | --- |
| Image size | Pixel dimensions of the exported PNG. |
| Error correction | `L` → `H`. Higher levels survive scuffs and partial covering, at the cost of a denser code. |
| Code color / Background | Any two colors. Keep the contrast strong — scanners need it. |
| Quiet zone | Blank border in modules. The spec calls for 4; below 2 many scanners fail. |

## Files

| File | Purpose |
| --- | --- |
| `index.html` | Page structure and form controls. |
| `styles.css` | Design tokens, layout, light and dark themes. |
| `app.js` | Encoding, canvas drawing, PNG/SVG export, history. |
| `vendor/qrcode-generator.js` | [qrcode-generator](https://github.com/kazuhikoarase/qrcode-generator) 1.4.4 (MIT), vendored so the page works offline. |

## How it works

`app.js` asks the library for a QR model, then draws it itself rather than using
the library's renderer. That keeps full control over colors, quiet zone, and
export size.

The drawing math matters for scannability: module width is rounded **down** to a
whole number of pixels and the result is centred on the canvas, so every module
lands on exact pixel boundaries. Fractional module widths produce soft edges that
cheap scanners misread.

SVG export walks the same module grid and emits one `<path>` in module
coordinates, so the file is compact and resolution-independent.

## Verified

Rendering was checked by round-tripping: draw the code, then decode the pixels
back with [jsQR](https://github.com/cozmo/jsQR). Five combinations of size,
error-correction level, and quiet zone all decoded back to the original URL.

## Browser support

Any current browser. Two graceful degradations:

- **Copy image** needs the async Clipboard API. Where it's missing or blocked,
  the app says so and points you at Save PNG.
- **Recent codes** needs `localStorage`. In private windows it's skipped
  silently — generating and saving still work.
