import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative, resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

// Pages must not put a raw HTTP status in text a person reads: "(403)", "(HTTP 500)", "could not be loaded (404)", "Request failed (${res.status})".
// Say what happened instead, with the shared classifier (lib/apiProblems.ts).
export const RAW_STATUS_TEXT =
  /\(\s*(?:HTTP\s*)?\$\{[^}]*(?<![A-Za-z])status(?:Text)?\b[^}]*\}[^)`]*\)|\bHTTP\s*\$\{[^}]*(?<![A-Za-z])status\b|\((?:HTTP\s*)?(?:40[0-9]|50[0-9])\)|could not be loaded \(/;

const SRC = resolve(__dirname, '../src');

function sources(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name);
    if (statSync(path).isDirectory()) return sources(path);
    return /\.(ts|tsx)$/.test(name) ? [path] : [];
  });
}

describe('no raw HTTP status in user-facing text', () => {
  it('finds no status code in a message template under src/', () => {
    const offenders = sources(SRC).flatMap((file) =>
      readFileSync(file, 'utf8')
        .split('\n')
        .map((line, index) => ({ line, number: index + 1 }))
        .filter(({ line }) => RAW_STATUS_TEXT.test(line))
        .map(({ line, number }) => `${relative(SRC, file)}:${number}: ${line.trim()}`),
    );
    expect(offenders, `Replace these with failureMessage()/problemError() from lib/apiProblems.ts:\n${offenders.join('\n')}`).toEqual([]);
  });

  it('would catch the patterns pages used before', () => {
    for (const text of [
      "throw new Error(`Vendors could not be loaded (${res.status}).`)",
      "setError('Vendors could not be loaded (403).')",
      'throw new Error(`Request failed (${response.status})`)',
      'detail = `HTTP ${res.status}`',
      'Unable to load reports (HTTP 500).',
      'Failed to load invoices (${res.status} ${res.statusText})',
    ]) {
      expect(RAW_STATUS_TEXT.test(text), text).toBe(true);
    }
    for (const text of ['`${capture.id}:${capture.status}:${capture.acceptedAt}`', "status === 403", 'The bill could not be loaded.']) {
      expect(RAW_STATUS_TEXT.test(text), text).toBe(false);
    }
  });
});
