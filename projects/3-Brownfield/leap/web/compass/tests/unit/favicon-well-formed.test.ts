import { describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * A favicon linked via `<link rel="icon" href="...">` is loaded by the browser as a standalone
 * XML document, not parsed by the lenient HTML parser the way an inline `<svg>` would be. An XML
 * comment containing a double hyphen (`--`) is not well-formed (forbidden by the XML spec), so a
 * browser encountering one here fails to parse the whole file and silently shows no icon at all
 * -- the HTTP layer still answers 200, so the failure is invisible to anything that only checks
 * the response status (issue #580).
 */
const FAVICON_PATH = join(__dirname, '../../public/favicon.svg');

describe('Compass favicon.svg is well-formed XML', () => {
  it('parses without a parsererror', () => {
    const svg = readFileSync(FAVICON_PATH, 'utf8');
    const doc = new DOMParser().parseFromString(svg, 'image/svg+xml');
    const parseError = doc.querySelector('parsererror');
    expect(parseError).toBeNull();
  });

  it('contains no double hyphen inside its XML comments', () => {
    const svg = readFileSync(FAVICON_PATH, 'utf8');
    const comments = [...svg.matchAll(/<!--([\s\S]*?)-->/g)].map((match) => match[1]);
    const offendingComments = comments.filter((comment) => comment.includes('--'));
    expect(offendingComments).toEqual([]);
  });
});
