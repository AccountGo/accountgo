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

const okAuthResponse = () =>
  new Response(
    JSON.stringify({
      accessToken: 'tok',
      expiresIn: 900,
      user: { id: 'u1', email: 'a@b.com', firstName: 'A', lastName: 'B', roles: [] },
      organizations: [{ slug: 'acme', name: 'Acme', role: 'Owner', mayNeedSetup: false }],
    }),
    { status: 200, headers: { 'Content-Type': 'application/json' } }
  );

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
        return okAuthResponse();
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

    await waitFor(() => {
      expect(fetchMock).toHaveBeenCalledTimes(1);
    });
    expect(events).toEqual(['fetch:/api/auth/mfa/webauthn/assertion/options']);

    await new Promise((r) => setTimeout(r, 0));

    events.push('click');
    fireEvent.click(getByRole('button', { name: /Verify with Passkey/i }));

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

  it('triggers a fresh prefetch after a failed assertion/complete response', async () => {
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
      if (url === '/api/auth/mfa/webauthn/assertion/complete') {
        return new Response(JSON.stringify({ detail: 'Assertion rejected.' }), {
          status: 400,
          headers: { 'Content-Type': 'application/json' },
        });
      }
      return new Response(null, { status: 404 });
    });
    globalThis.fetch = fetchMock as any;

    Object.defineProperty(navigator, 'credentials', {
      value: { get: vi.fn(async () => makeAssertion()) },
      configurable: true,
    });

    const { getByRole, findByRole } = render(
      <MfaChallenge
        mfaToken="mfa-tok-complete-fail"
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

    const alert = await findByRole('alert');
    expect(alert.textContent).toContain('Assertion rejected.');
    await waitFor(() => {
      expect(optionsCalls).toBe(2);
    });
  });

  it('awaits an in-flight prefetch on click without issuing a second options fetch', async () => {
    let optionsCalls = 0;
    let resolveOptionsFetch!: (res: Response) => void;
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const url = String(input);
      if (url === '/api/auth/mfa/webauthn/assertion/options') {
        optionsCalls++;
        return new Promise<Response>((resolve) => {
          resolveOptionsFetch = resolve;
        });
      }
      if (url === '/api/auth/mfa/webauthn/assertion/complete') {
        return Promise.resolve(okAuthResponse());
      }
      return Promise.resolve(new Response(null, { status: 404 }));
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
        mfaToken="mfa-tok-inflight"
        methods={['webauthn']}
        onSuccess={onSuccess}
        onCancel={() => {}}
      />
    );

    await waitFor(() => {
      expect(optionsCalls).toBe(1);
    });

    // Click while the initial prefetch is still in flight.
    fireEvent.click(getByRole('button', { name: /Verify with Passkey/i }));
    expect(optionsCalls).toBe(1);
    expect(getMock).not.toHaveBeenCalled();

    resolveOptionsFetch(
      new Response(JSON.stringify(makeOptions('Y2hhbGxlbmdlLWluZmxpZ2h0')), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    );

    await waitFor(() => {
      expect(onSuccess).toHaveBeenCalledTimes(1);
    });
    expect(optionsCalls).toBe(1);
    expect(getMock).toHaveBeenCalledTimes(1);
  });

  it('drops old options and prefetches with the new token when mfaToken changes', async () => {
    const authHeaders: string[] = [];
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (url === '/api/auth/mfa/webauthn/assertion/options') {
        const auth = new Headers(init?.headers).get('Authorization') ?? '';
        authHeaders.push(auth);
        return new Response(JSON.stringify(makeOptions(auth.includes('tok-b') ? 'bmV3LWNoYWxsZW5nZQ' : 'b2xkLWNoYWxsZW5nZQ')), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        });
      }
      if (url === '/api/auth/mfa/webauthn/assertion/complete') {
        return okAuthResponse();
      }
      return new Response(null, { status: 404 });
    });
    globalThis.fetch = fetchMock as any;

    const seenChallenges: string[] = [];
    Object.defineProperty(navigator, 'credentials', {
      value: {
        get: vi.fn(async (opts: any) => {
          seenChallenges.push(new TextDecoder().decode(opts.publicKey.challenge));
          return makeAssertion();
        }),
      },
      configurable: true,
    });

    const onSuccess = vi.fn();
    const { rerender, getByRole } = render(
      <MfaChallenge mfaToken="tok-a" methods={['webauthn']} onSuccess={onSuccess} onCancel={() => {}} />
    );

    await waitFor(() => {
      expect(authHeaders).toEqual(['Bearer tok-a']);
    });
    await new Promise((r) => setTimeout(r, 0));

    rerender(
      <MfaChallenge mfaToken="tok-b" methods={['webauthn']} onSuccess={onSuccess} onCancel={() => {}} />
    );

    await waitFor(() => {
      expect(authHeaders).toEqual(['Bearer tok-a', 'Bearer tok-b']);
    });
    await new Promise((r) => setTimeout(r, 0));

    fireEvent.click(getByRole('button', { name: /Verify with Passkey/i }));
    await waitFor(() => {
      expect(onSuccess).toHaveBeenCalledTimes(1);
    });
    expect(seenChallenges).toEqual(['new-challenge']);
  });

  it('prefetches when switching from TOTP mode to passkey mode', async () => {
    const authHeaders: string[] = [];
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (url === '/api/auth/mfa/webauthn/assertion/options') {
        authHeaders.push(new Headers(init?.headers).get('Authorization') ?? '');
        return new Response(JSON.stringify(makeOptions('Y2hhbGxlbmdl')), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        });
      }
      return new Response(null, { status: 404 });
    });
    globalThis.fetch = fetchMock as any;

    const { getByRole, rerender } = render(
      <MfaChallenge
        mfaToken="tok-1"
        methods={['webauthn', 'totp']}
        onSuccess={() => {}}
        onCancel={() => {}}
      />
    );

    await waitFor(() => {
      expect(authHeaders).toEqual(['Bearer tok-1']);
    });

    // Switch to TOTP mode, rotate token while in TOTP (must not prefetch in TOTP mode).
    fireEvent.click(getByRole('button', { name: /Use an authenticator code instead/i }));
    rerender(
      <MfaChallenge
        mfaToken="tok-2"
        methods={['webauthn', 'totp']}
        onSuccess={() => {}}
        onCancel={() => {}}
      />
    );
    await new Promise((r) => setTimeout(r, 10));
    expect(authHeaders).toEqual(['Bearer tok-1']);

    // Switch back to passkey mode: triggers prefetch with tok-2.
    fireEvent.click(getByRole('button', { name: /Sign in with Passkey/i }));
    await waitFor(() => {
      expect(authHeaders).toEqual(['Bearer tok-1', 'Bearer tok-2']);
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
        return okAuthResponse();
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

  it('does not re-prefetch when options request fails with 401 (expired MFA token)', async () => {
    let optionsCalls = 0;
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url === '/api/auth/mfa/webauthn/assertion/options') {
        optionsCalls++;
        return new Response(JSON.stringify({ detail: 'Session expired.' }), {
          status: 401,
          headers: { 'Content-Type': 'application/json' },
        });
      }
      return new Response(null, { status: 404 });
    });
    globalThis.fetch = fetchMock as any;

    const getMock = vi.fn(async () => makeAssertion());
    Object.defineProperty(navigator, 'credentials', {
      value: { get: getMock },
      configurable: true,
    });

    const { getByRole, findByRole } = render(
      <MfaChallenge
        mfaToken="mfa-tok-expired"
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

    const alert = await findByRole('alert');
    expect(alert.textContent).toContain('Session expired.');
    await new Promise((r) => setTimeout(r, 20));
    // 1 initial prefetch + 1 click attempt = 2; no automatic re-prefetch after options failure.
    expect(optionsCalls).toBe(2);
    expect(getMock).not.toHaveBeenCalled();
  });
});
