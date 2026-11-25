import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, renderHook } from '@testing-library/react';
import { AuthProvider, useAuth } from '../src/auth/AuthContext';
import { withSuppressedEntitlementDenial } from '../src/lib/entitlementEvents';
import { PERMISSION_DENIED_EVENT, announcePermissionDenial, type PermissionDeniedDetail } from '../src/lib/permissionDenialEvents';

// authFetch is the one door for every signed-in call, so announcing here covers every action in the app.
describe('permission denial announcements from authFetch', () => {
  let originalFetch: typeof globalThis.fetch;
  let events: PermissionDeniedDetail[] = [];
  const onDenied = (event: Event) => events.push((event as CustomEvent<PermissionDeniedDetail>).detail);

  const denied = (permission = 'purchases.bills.post', status = 403) =>
    new Response(JSON.stringify({
      type: 'https://accountgo.ph/errors/permission-denied', title: 'Access Denied', status,
      detail: `You do not have the required permission '${permission}' to perform this action.`,
      code: 'PERMISSION_DENIED', requiredPermission: permission,
    }), { status, headers: { 'Content-Type': 'application/problem+json' } });

  const send = async (url: string, init?: RequestInit) => {
    const { result } = renderHook(() => useAuth(), { wrapper: ({ children }) => <AuthProvider>{children}</AuthProvider> });
    const res = await result.current.authFetch(url, init);
    await new Promise((resolve) => setTimeout(resolve, 10));
    return res;
  };

  beforeEach(() => {
    events = [];
    window.addEventListener(PERMISSION_DENIED_EVENT, onDenied);
    originalFetch = globalThis.fetch;
  });
  afterEach(() => {
    cleanup();
    window.removeEventListener(PERMISSION_DENIED_EVENT, onDenied);
    globalThis.fetch = originalFetch;
    vi.restoreAllMocks();
  });

  it('a denied write announces it as an action, with the permission and no document content', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue(denied());

    const res = await send('/api/Purchasing/ApproveBill', { method: 'POST', body: JSON.stringify({ secret: 'invoice text' }) });

    expect(res.status).toBe(403);
    expect(events).toHaveLength(1);
    expect(events[0].action).toBe(true);
    expect(events[0].problem.category).toBe('permission-denied');
    expect(events[0].problem.requiredPermission).toBe('purchases.bills.post');
    expect(JSON.stringify(events[0])).not.toContain('invoice text');
  });

  it('a denied read is announced as not an action, so it refreshes permissions but shows no notice', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue(denied('purchases.vendors.view'));

    await send('/api/Purchasing/Vendors');

    expect(events).toHaveLength(1);
    expect(events[0].action).toBe(false);
  });

  it('ignores other kinds of failure: plan, quota, an unnamed 403, a 500 and a success', async () => {
    for (const response of [
      new Response(JSON.stringify({ code: 'QUOTA_EXCEEDED' }), { status: 403, headers: { 'Content-Type': 'application/json' } }),
      new Response(JSON.stringify({ code: 'mfa_step_up_required' }), { status: 403, headers: { 'Content-Type': 'application/json' } }),
      new Response('Forbidden', { status: 403 }),
      new Response(JSON.stringify({ code: 'PERMISSION_DENIED' }), { status: 500, headers: { 'Content-Type': 'application/json' } }),
      new Response('{}', { status: 200, headers: { 'Content-Type': 'application/json' } }),
    ]) {
      globalThis.fetch = vi.fn().mockResolvedValue(response);
      await send('/api/anything', { method: 'POST' });
    }

    expect(events).toHaveLength(0);
  });

  it('never announces for sign-in routes', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue(denied());

    await send('/api/auth/mfa/verify', { method: 'POST' });
    await send('/api/auth/me');

    expect(events).toHaveLength(0);
  });

  it('honours the switch that flows with their own denial copy already use', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue(denied());

    await withSuppressedEntitlementDenial(async () => { await send('/api/Purchasing/ApproveBill', { method: 'POST' }); });

    expect(events).toHaveLength(0);
  });

  it('writes nothing to the console, so no permission key reaches the logs', async () => {
    const spies = (['log', 'info', 'warn', 'error', 'debug'] as const).map((level) => vi.spyOn(console, level).mockImplementation(() => {}));
    globalThis.fetch = vi.fn().mockResolvedValue(denied());

    await send('/api/Purchasing/ApproveBill', { method: 'POST' });
    await announcePermissionDenial(denied(), { action: true });

    for (const spy of spies) {
      expect(JSON.stringify(spy.mock.calls)).not.toContain('purchases.bills.post');
    }
  });
});
