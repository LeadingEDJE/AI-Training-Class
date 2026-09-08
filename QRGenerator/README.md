# QR Code Generator

Type a URL, get a scannable QR code. Plain HTML, CSS, and JavaScript — no build
step, no dependencies, no network.

**To run it:** open `index.html` in a browser. That's the whole setup.

## Files

| File            | What it does                                                  |
| --------------- | ------------------------------------------------------------- |
| `index.html`    | Structure and controls                                        |
| `styles.css`    | Layout and theming (follows your system light/dark preference) |
| `qr-encoder.js` | The QR encoder — no DOM, no dependencies                      |
| `app.js`        | Reads the controls, paints the canvas, handles the download    |

## Features

- Live preview as you type
- Error correction levels L / M / Q / H
- Custom foreground and background colors, with a warning when the contrast is
  too low or the colors are inverted (most scanners need dark on light)
- Adjustable size, applied to both the preview and the downloaded PNG
- Download as PNG

## The encoder

`qr-encoder.js` implements ISO/IEC 18004 from scratch rather than calling a
library. It exposes one function, which touches no browser API and so can be
tested under Node:

```js
generateQR('https://example.com', { ecLevel: 'M' });
// { size, modules: boolean[][], version, maskId, ecLevel, byteLength }
```

It covers versions 1–40 at all four error-correction levels, in **byte mode**
only. Byte mode encodes any UTF-8 text, which is what URLs need; numeric and
alphanumeric modes would pack digits and uppercase text more tightly, but add a
lot of machinery for a case this app doesn't have. A consequence worth knowing:
for an all-digit or all-uppercase payload, this encoder picks a slightly larger
version than a full-featured one would.

The file is organised in the order the spec applies the steps, and each section
is numbered:

1. **Galois field GF(256)** — log/antilog tables that turn multiplication into
   addition of exponents
2. **Reed–Solomon** — build the generator polynomial, divide, keep the remainder
3. **Capacity tables** — block layout for each of the 160 version/level pairs
4. **Data encoding** — mode indicator, character count, payload, padding
5. **Block splitting and interleaving** — what makes burst damage survivable
6. **Matrix construction** — finder, alignment, and timing patterns
7. **Data placement** — the right-to-left zigzag
8. **Masking** — try all 8, score with four penalty rules, keep the best
9. **Format and version information** — BCH-protected metadata

### Four places this is easy to get wrong

These are the bugs that produce a symbol which looks perfectly correct and scans
as nothing at all. They're marked in the source too.

- **Format bits go MSB-first.** Writing them LSB-first keeps the two copies
  self-consistent, so the symbol looks fine — only a real decoder notices.
- **The EC level bits are not 0/1/2/3.** They are L=01, M=00, Q=11, H=10.
- **Column 6 is skipped** during data placement, shifting every column to its
  left by one.
- **Interleaving is by codeword index across blocks**, not block-by-block
  concatenation.

## How this was verified

The encoder was checked against three independent oracles:

- **Exact matrix equality** against the `qrcode` npm package across all 160
  version/level combinations, at capacity and under it, with byte mode and the
  mask forced so neither is a confound
- **Mask selection**, confirmed to be the argmin of the spec's penalty rules
- **Round-trip decoding** through `jsQR` on realistic payloads, including
  multi-byte UTF-8

The page itself was driven in headless Chrome over `file://`, reading the real
canvas pixels back and decoding them — covering live typing, every EC level, the
size slider, colors, the contrast warnings, empty input, over-capacity input,
reset, and the PNG export.

One deliberate deviation: `qrcode`'s penalty rule 4 is `|ceil(pct/5) - 10| × 10`,
which scores a 51%-dark symbol as 10 where the spec scores it 0. That makes its
chosen mask differ from ours on some inputs. Both produce valid codes; this
encoder follows the spec.
