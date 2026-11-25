import { describe, expect, it } from 'vitest';
import { safeReturnPath } from '../src/lib/safeReturnPath';

describe('safeReturnPath', () => {
  it('keeps an in-app path and its query', () => {
    expect(safeReturnPath('/org/acme/sales/invoices/inv_1', '?tab=notes')).toBe('/org/acme/sales/invoices/inv_1?tab=notes');
    expect(safeReturnPath('/org/acme/dashboard')).toBe('/org/acme/dashboard');
  });

  it.each([
    'https://evil.example/x', '//evil.example', '/\\evil.example', '\\\\evil.example', 'org/acme', '', '/javascript:alert(1)',
    '/%2F%2Fevil.example', '/%5Cevil.example', '/org/%2e%2e/%2e%2e/x', '/org/../x', '/a\nb', '/a\u0000b', '/a%0d%0ab',
  ])('rejects %j', (path) => {
    expect(safeReturnPath(path)).toBeNull();
  });

  it.each(['/login', '/login/extra', '/signup', '/verify', '/auth/callback', '/select-org', '/LOGIN', '/%6cogin'])('never returns to the auth screen %s', (path) => {
    expect(safeReturnPath(path)).toBeNull();
  });

  it('rejects non-string and malformed input, an over-long path and a query without ?', () => {
    expect(safeReturnPath(undefined)).toBeNull();
    expect(safeReturnPath({ a: 1 })).toBeNull();
    expect(safeReturnPath('/' + 'a'.repeat(3000))).toBeNull();
    expect(safeReturnPath('/org/acme', 'evil=1')).toBeNull();
    expect(safeReturnPath('/%E0%A4%A')).toBeNull();
  });
});
