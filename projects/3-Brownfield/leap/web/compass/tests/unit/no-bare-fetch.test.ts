import { describe, expect, it } from 'vitest';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';

/**
 * Enforces the project-wide rule: every API call goes
 * through `apiFetch`, never a bare `fetch()`. `lib/api-url.ts` is exempt — its own `fetch()` call
 * IS the wrapper being defined.
 */
const SRC_DIR = join(__dirname, '../../src');
const ALLOWED_BARE_FETCH_FILES = ['lib/api-url.ts'];

function listSourceFiles(dir: string): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) {
      return listSourceFiles(full);
    }
    return /\.(ts|tsx)$/.test(entry) ? [full] : [];
  });
}

describe('no bare fetch() outside the authenticated apiFetch wrapper', () => {
  it('finds no file under src/ calling fetch() directly, except api-url.ts itself', () => {
    const offenders: string[] = [];
    for (const file of listSourceFiles(SRC_DIR)) {
      const relative = file.slice(SRC_DIR.length + 1).replace(/\\/g, '/');
      if (ALLOWED_BARE_FETCH_FILES.includes(relative)) {
        continue;
      }
      const content = readFileSync(file, 'utf8');
      const bareFetchLines = content
        .split('\n')
        .filter((line) => /\bfetch\(/.test(line) && !/apiFetch\(/.test(line));
      if (bareFetchLines.length > 0) {
        offenders.push(relative);
      }
    }
    expect(offenders).toEqual([]);
  });
});
