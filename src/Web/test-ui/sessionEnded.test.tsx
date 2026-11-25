import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, cleanup, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { AuthProvider, useAuth } from '../src/auth/AuthContext';
import { ProtectedRoute } from '../src/components/ProtectedRoute';
import { actionNotices } from '../src/lib/actionDenialNotices';
import { clearAccessDirectoryCache } from '../src/lib/accessDirectory';
import { throttledRefresh } from '../src/lib/permissionRefresh';

const signedIn = (token = 'tok-1') => new Response(JSON.stringify({
  accessToken: token,
  user: { id: 'u1', email: 'a@example.test', firstName: 'A', lastName: 'B' },
}), { status: 200, headers: { 'Content-Type': 'application/json' } });

const LoginStub = () => {
  const state = useLocation().state as { notice?: string; from?: { pathname: string; search?: string } } | null;
  return <div><p data-testid="login">login</p><p data-testid="notice">{state?.notice ?? ''}</p><p data-testid="from">{state?.from ? state.from.pathname + (state.from.search ?? '') : ''}</p></div>;
};

let auth: ReturnType<typeof useAuth>;
const Grab = () => { auth = useAuth(); return null; };

function mount(startAt = '/org/acme/sales/invoices/inv_1?tab=notes') {
  return render(
    <MemoryRouter initialEntries={[startAt]}>
      <AuthProvider>
        <Grab />
        <Routes>
          <Route path="/login" element={<LoginStub />} />
          <Route element={<ProtectedRoute />}>
            <Route path="/org/*" element={<p data-testid="page">page</p>} />
          </Route>
        </Routes>
      </AuthProvider>
    </MemoryRouter>,
  );
}

describe('a session that ends because the refresh failed', () => {
  let originalFetch: typeof globalThis.fetch;
  let refreshCalls = 0;
  beforeEach(() => { originalFetch = globalThis.fetch; refreshCalls = 0; actionNotices.reset(); clearAccessDirectoryCache(); });
  afterEach(() => { cleanup(); globalThis.fetch = originalFetch; vi.restoreAllMocks(); });

  // First refresh (page load) succeeds; afterwards the API says 401 and the refresh is refused.
  const revokedAfterBoot = () => {
    let booted = false;
    globalThis.fetch = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.includes('/api/auth/refresh')) {
        refreshCalls += 1;
        if (!booted) { booted = true; return signedIn(); }
        return new Response('{}', { status: 401 });
      }
      return new Response('{}', { status: 401 });
    }) as typeof fetch;
  };

  it('signs out once with the "session ended" notice and keeps the page to return to', async () => {
    revokedAfterBoot();
    mount();
    await screen.findByTestId('page');

    await act(async () => { await auth.authFetch('/api/Sales/Invoices'); });

    expect(await screen.findByTestId('login')).toBeTruthy();
    expect(screen.getByTestId('notice').textContent).toBe("Your session ended. Sign in again to continue. Anything you hadn't saved wasn't kept.");
    expect(screen.getByTestId('from').textContent).toBe('/org/acme/sales/invoices/inv_1?tab=notes');
  });

  it('many requests failing together cause one refresh attempt and one notice', async () => {
    revokedAfterBoot();
    mount();
    await screen.findByTestId('page');
    refreshCalls = 0;

    await act(async () => {
      await Promise.all(Array.from({ length: 6 }, (_, i) => auth.authFetch(`/api/Sales/Invoices?page=${i}`)));
    });

    expect(refreshCalls).toBe(1);
    expect(screen.getAllByTestId('notice')).toHaveLength(1);
    expect(screen.getByTestId('notice').textContent).toContain('Your session ended');
  });

  it('a visitor who was never signed in gets no notice', async () => {
    globalThis.fetch = vi.fn(async () => new Response('{}', { status: 401 })) as typeof fetch;
    mount();

    await screen.findByTestId('login');

    expect(screen.getByTestId('notice').textContent).toBe('');
  });

  it('forgets action notices, member lists and permission refresh timers when the session ends', async () => {
    revokedAfterBoot();
    mount();
    await screen.findByTestId('page');
    window.dispatchEvent(new CustomEvent('accountgo:permission-denied', { detail: { action: true, problem: { category: 'permission-denied', status: 403, requiredPermission: 'purchases.bills.post' } } }));
    expect(actionNotices.getSnapshot()).toHaveLength(1);
    const refresh = vi.fn().mockResolvedValue(true);
    throttledRefresh('acme:u1', refresh, { now: () => 1000 });
    await act(async () => { await Promise.resolve(); });

    await act(async () => { await auth.authFetch('/api/Sales/Invoices'); });
    await screen.findByTestId('login');

    expect(actionNotices.getSnapshot()).toHaveLength(0);
    // the refresh timer is gone: the same call a few seconds later runs again instead of being throttled
    throttledRefresh('acme:u1', refresh, { now: () => 2000 });
    await waitFor(() => expect(refresh).toHaveBeenCalledTimes(2));
  });

  it('a user sign-out also clears the action notices', async () => {
    globalThis.fetch = vi.fn(async (input: RequestInfo | URL) => String(input).includes('/api/auth/refresh') ? signedIn() : new Response('{}', { status: 200 })) as typeof fetch;
    mount();
    await screen.findByTestId('page');
    window.dispatchEvent(new CustomEvent('accountgo:permission-denied', { detail: { action: true, problem: { category: 'permission-denied', status: 403, requiredPermission: 'purchases.bills.post' } } }));

    await act(async () => { await auth.logout(); });

    expect(actionNotices.getSnapshot()).toHaveLength(0);
  });
});
