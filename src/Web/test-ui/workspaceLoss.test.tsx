import React from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, cleanup, renderHook, waitFor } from '@testing-library/react';
import { AuthProvider, useAuth } from '../src/auth/AuthContext';
import { actionNotices } from '../src/lib/actionDenialNotices';
import { clearAccessDirectoryCache, useAccessDirectory } from '../src/lib/accessDirectory';
import { throttledRefresh } from '../src/lib/permissionRefresh';
import { useWorkspaceLoss } from '../src/lib/useWorkspaceLoss';
import { WORKSPACE_ACCESS_LOST_EVENT } from '../src/lib/workspaceAccessEvents';

const lost = (status = 404, body: object = { title: 'Workspace not found', status: 404, code: 'WORKSPACE_NOT_FOUND' }) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/problem+json' } });

describe('workspace access lost mid-session', () => {
  let originalFetch: typeof globalThis.fetch;
  let events: string[] = [];
  const onLost = (event: Event) => events.push((event as CustomEvent<{ slug: string }>).detail.slug);

  const send = async (path: string, url: string, init?: RequestInit) => {
    window.history.pushState({}, '', path);
    const { result } = renderHook(() => useAuth(), { wrapper: ({ children }) => <AuthProvider>{children}</AuthProvider> });
    const res = await result.current.authFetch(url, init);
    await new Promise((resolve) => setTimeout(resolve, 10));
    return res;
  };

  beforeEach(() => { events = []; originalFetch = globalThis.fetch; window.addEventListener(WORKSPACE_ACCESS_LOST_EVENT, onLost); actionNotices.reset(); clearAccessDirectoryCache(); });
  afterEach(() => { cleanup(); window.removeEventListener(WORKSPACE_ACCESS_LOST_EVENT, onLost); globalThis.fetch = originalFetch; vi.restoreAllMocks(); window.history.pushState({}, '', '/'); });

  it('a WORKSPACE_NOT_FOUND on an org-scoped call announces the loss for that workspace', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue(lost());
    await send('/org/acme/sales/invoices', '/api/Sales/Invoices');
    expect(events).toEqual(['acme']);
  });

  it('a record 404 (not the workspace) announces nothing', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue(lost(404, { title: 'Not Found', status: 404 }));
    await send('/org/acme/sales/invoices/inv_9', '/api/Sales/Invoices/inv_9');
    expect(events).toEqual([]);
  });

  it('never for calls that are not org-scoped: the workspace list, invitations, auth routes', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue(lost());
    await send('/org/acme/dashboard', '/api/organizations');
    await send('/org/acme/dashboard', '/api/invitations/pending');
    await send('/org/acme/dashboard', '/api/auth/session');
    await send('/select-org', '/api/Sales/Invoices'); // not on an org route: no org header
    expect(events).toEqual([]);
  });

  describe('the shell hook', () => {
    const fire = (slug: string) => act(() => { window.dispatchEvent(new CustomEvent(WORKSPACE_ACCESS_LOST_EVENT, { detail: { slug } })); });

    it('leaves once for many parallel failures and ignores other workspaces', () => {
      const onLeave = vi.fn();
      renderHook(() => useWorkspaceLoss('acme', onLeave));
      fire('other');
      expect(onLeave).not.toHaveBeenCalled();
      fire('ACME'); fire('acme'); fire('acme');
      expect(onLeave).toHaveBeenCalledTimes(1);
    });

    it('can leave again after the workspace changes', () => {
      const onLeave = vi.fn();
      const { rerender } = renderHook(({ slug }) => useWorkspaceLoss(slug, onLeave), { initialProps: { slug: 'acme' } });
      fire('acme');
      rerender({ slug: 'beta' });
      fire('beta');
      expect(onLeave).toHaveBeenCalledTimes(2);
    });

    it('clears action notices, the member-list cache and permission refresh timers, then leaves', async () => {
      window.dispatchEvent(new CustomEvent('accountgo:permission-denied', { detail: { action: true, problem: { category: 'permission-denied', status: 403, requiredPermission: 'purchases.bills.post' } } }));
      expect(actionNotices.getSnapshot()).toHaveLength(1);
      const run = vi.fn().mockResolvedValue(true);
      await throttledRefresh('acme:u1', run, { now: () => 1000 });
      const order: string[] = [];
      renderHook(() => useWorkspaceLoss('acme', () => order.push(`leave:notices=${actionNotices.getSnapshot().length}`)));

      fire('acme');

      expect(order).toEqual(['leave:notices=0']);
      await throttledRefresh('acme:u1', run, { now: () => 2000 }); // not throttled: the timer was reset
      expect(run).toHaveBeenCalledTimes(2);
    });

    it('forgets the cached member list, so the next view reads it again', async () => {
      const calls: string[] = [];
      globalThis.fetch = vi.fn(async (input: RequestInfo | URL) => { calls.push(String(input)); return new Response('[]', { status: 200 }); }) as typeof fetch;
      window.history.pushState({}, '', '/org/acme/purchases');
      const wrapper = ({ children }: { children: React.ReactNode }) => <AuthProvider>{children}</AuthProvider>;
      const first = renderHook(() => useAccessDirectory('purchases.bills.post'), { wrapper });
      await waitFor(() => expect(calls.filter((url) => url.endsWith('/members'))).toHaveLength(1));
      first.unmount();
      const second = renderHook(() => useAccessDirectory('purchases.bills.post'), { wrapper });
      await act(async () => { await new Promise((resolve) => setTimeout(resolve, 20)); });
      expect(calls.filter((url) => url.endsWith('/members'))).toHaveLength(1); // cached
      second.unmount();

      renderHook(() => useWorkspaceLoss('acme', () => undefined));
      fire('acme');
      renderHook(() => useAccessDirectory('purchases.bills.post'), { wrapper });

      await waitFor(() => expect(calls.filter((url) => url.endsWith('/members'))).toHaveLength(2));
    });
  });
});
