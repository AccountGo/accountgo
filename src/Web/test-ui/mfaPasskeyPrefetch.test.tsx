import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { render, fireEvent, waitFor, cleanup } from '@testing-library/react';
import { MfaChallenge } from '../src/components/mfa/MfaChallenge';

const makeOptions = (challenge: string) => ({
  challenge,
  rpId: 'accountgo.dev',
  allowCredentials: [{ id: 'Y3JlZC0x', type: 'public-key' }],
});

const makeAssertion = () => ({
  id: 'cred-1',
  rawId: new Uint8Array([1, 2, 3]).buffer,
  type: 'public-key',
  response: {
    authenticatorData: new Uint8Array([4, 5]).buffer,
    clientDataJSON: new Uint8Array([6, 7]).buffer,
    signature: new Uint8Array([8, 9]).buffer,
    userHandle: null,
  },
  getClientExtensionResults: () => ({}),
});

describe('MfaChallenge passkey options prefetch', () => {
  const originalFetch = globalThis.fetch;
  const originalPublicKeyCredential = (window as any).PublicKeyCredential;
  const originalCredentials = navigator.credentials;

  beforeEach(() => {
    (window as any).PublicKeyCredential = function PublicKeyCredential() {};
  });

  afterEach(() => {
    cleanup();
    globalThis.fetch = originalFetch;
    (window as any).PublicKeyCredential = originalPublicKeyCredential;
    Object.defineProperty(navigator, 'credentials', {
      value: originalCredentials,
      configurable: true,
    });
    vi.restoreAllMocks();
  });

  it('prefetches options once on mount and calls navigator.credentials.get on click with no fetch in between', async () => {
    const events: string[] = [];
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      events.push(`fetch:${url}`);
      if (url === '/api/auth/mfa/webauthn/assertion/options') {
        return new Response(JSON.stringify(makeOptions('Y2hhbGxlbmdlLTE')), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        });
      }
      if (url === '/api/auth/mfa/webauthn/assertion/complete') {
        return new Response(
          JSON.stringify({
            accessToken: 'tok',
            expiresIn: 900,
            user: { id: 'u1', email: 'a@b.com', firstName: 'A', lastName: 'B', roles: [] },
            organizations: [{ slug: 'acme', name: 'Acme', role: 'Owner', mayNeedSetup: false }],
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }
      return new Response(null, { status: 404 });
    });
    globalThis.fetch = fetchMock as any;

    const getMock = vi.fn(async () => {
      events.push('credentials.get');
      return makeAssertion();
    });
    Object.defineProperty(navigator, 'credentials', {
      value: { get: getMock },
      configurable: true,
    });

    const onSuccess = vi.fn();
    const { getByRole } = render(
      <MfaChallenge
        mfaToken="mfa-tok-1"
        methods={['webauthn', 'totp']}
        onSuccess={onSuccess}
        onCancel={() => {}}
      />
    );

    // Prefetch runs once on mount.
    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalledTimes(1);
    });
    expect(events).toEqual(['fetch:/api/auth/mfa/webauthn/assertion/options']);

    // Flush microtasks so prefetched options are cached and ready.
    await new Promise((r) => setTimeout(r, 0));

    events.push('click');
    fireEvent.click(getByRole('button', { name: /Verify with Passkey/i }));

    // navigator.credentials.get must be called synchronously inside the click handler,
    // with NO fetch between click and credentials.get.
    expect(getMock).toHaveBeenCalledTimes(1);
    expect(events.slice(0, 3)).toEqual([
      'fetch:/api/auth/mfa/webauthn/assertion/options',
      'click',
      'credentials.get',
    ]);

    await waitFor(() => {
      expect(onSuccess).toHaveBeenCalledTimes(1);
    });
    expect(events).toEqual([
      'fetch:/api/auth/mfa/webauthn/assertion/options',
      'click',
      'credentials.get',
      'fetch:/api/auth/mfa/webauthn/assertion/complete',
    ]);
  });

  it('triggers a fresh prefetch after a cancelled or failed passkey ceremony', async () => {
    let optionsCalls = 0;
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url === '/api/auth/mfa/webauthn/assertion/options') {
        optionsCalls++;
        return new Response(JSON.stringify(makeOptions(`Y2hhbGxlbmdlL${optionsCalls}`)), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        });
      }
      return new Response(null, { status: 404 });
    });
    globalThis.fetch = fetchMock as any;

    const getMock = vi.fn(async () => {
      throw new Error('The operation was cancelled.');
    });
    Object.defineProperty(navigator, 'credentials', {
      value: { get: getMock },
      configurable: true,
    });

    const { getByRole, findByRole } = render(
      <MfaChallenge
        mfaToken="mfa-tok-2"
        methods={['webauthn']}
        onSuccess={() => {}}
        onCancel={() => {}}
      />
    );

    await waitFor(() => {
      expect(optionsCalls).toBe(1);
    });
    await new Promise((r) => setTimeout(r, 0));

    fireEvent.click(getByRole('button', { name: /Verify with Passkey/i }));

    await findByRole('alert');
    await waitFor(() => {
      expect(optionsCalls).toBe(2);
    });
  });

  it('falls back to fetching options on click when the initial prefetch failed', async () => {
    let optionsCalls = 0;
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url === '/api/auth/mfa/webauthn/assertion/options') {
        optionsCalls++;
        if (optionsCalls === 1) {
          return new Response(JSON.stringify({ detail: 'Transient error' }), {
            status: 503,
            headers: { 'Content-Type': 'application/json' },
          });
        }
        return new Response(JSON.stringify(makeOptions('Y2hhbGxlbmdlLTJ')), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        });
      }
      if (url === '/api/auth/mfa/webauthn/assertion/complete') {
        return new Response(
          JSON.stringify({
            accessToken: 'tok',
            expiresIn: 900,
            user: { id: 'u1', email: 'a@b.com', firstName: 'A', lastName: 'B', roles: [] },
            organizations: [],
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }
      return new Response(null, { status: 404 });
    });
    globalThis.fetch = fetchMock as any;

    const getMock = vi.fn(async () => makeAssertion());
    Object.defineProperty(navigator, 'credentials', {
      value: { get: getMock },
      configurable: true,
    });

    const onSuccess = vi.fn();
    const { getByRole } = render(
      <MfaChallenge
        mfaToken="mfa-tok-3"
        methods={['webauthn']}
        onSuccess={onSuccess}
        onCancel={() => {}}
      />
    );

    await waitFor(() => {
      expect(optionsCalls).toBe(1);
    });
    await new Promise((r) => setTimeout(r, 0));

    fireEvent.click(getByRole('button', { name: /Verify with Passkey/i }));

    await waitFor(() => {
      expect(onSuccess).toHaveBeenCalledTimes(1);
    });
    expect(optionsCalls).toBe(2);
    expect(getMock).toHaveBeenCalledTimes(1);
  });
});
