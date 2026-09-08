# QR Code Generator

A small command-line utility that turns a URL into a QR code and saves it in
this project folder (`output/`).

## Setup

Requires [Node.js](https://nodejs.org/) 18 or newer.

```bash
cd projects/1-BusinessUtility/qr-generator
npm install
```

## Usage

Pass the URL as an argument:

```bash
node qr-gen.js https://www.leadingedje.com/careers
```

Or run it with no arguments and it will ask for one:

```bash
node qr-gen.js
# URL to encode: leadingedje.com
```

Each run prints a scannable preview in the terminal and writes a `.png` and a
`.svg` into `output/`, named after the URL (e.g.
`output/leadingedje-com-careers.png`). Existing files are never overwritten —
a repeat run adds `-2`, `-3`, and so on.

Bare hosts like `leadingedje.com` get `https://` added automatically. Other
schemes (`mailto:`, `tel:`) work too.

## Options

| Option | Default | Description |
| --- | --- | --- |
| `--name <base>` | derived from the URL | Base filename to use |
| `--out <dir>` | `./output` | Directory to write into |
| `--format <fmt>` | `both` | `png`, `svg`, or `both` |
| `--ecc <level>` | `M` | Error correction: `L`, `M`, `Q`, `H` |
| `--size <px>` | `512` | Width of the PNG in pixels |
| `--margin <mods>` | `2` | Quiet zone width, in modules |
| `-h`, `--help` | | Show usage |

Example — a large, high-redundancy PNG for print:

```bash
node qr-gen.js https://example.com --format png --ecc H --size 1024 --name flyer-qr
```

Higher error correction survives more smudging or damage, at the cost of a
denser code. `M` is a good default for screens and handouts.

## Tests

```bash
npm test
```

The tests generate real PNGs and decode them back with a QR reader to confirm
the output scans to the original URL.

## How it works

- [qr-gen.js](qr-gen.js) — argument parsing, URL normalization, filename
  slugging, and file output. Encoding is handled by the
  [`qrcode`](https://www.npmjs.com/package/qrcode) package.
- [test/qr-gen.test.js](test/qr-gen.test.js) — unit tests plus the decode
  round-trip, using Node's built-in test runner.
