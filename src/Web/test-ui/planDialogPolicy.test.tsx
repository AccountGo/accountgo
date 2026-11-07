// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, renderHook } from '@testing-library/react';
import { AuthProvider, useAuth } from '../src/auth/AuthContext';
import { ENTITLEMENT_DENIED_EVENT, withSuppressedEntitlementDenial, EntitlementProblem } from '../src/lib/entitlementEvents';

describe('authFetch planDialog policy', () => {
  let originalFetch: typeof globalThis.fetch;
  let dispatchedEvents: EntitlementProblem[] = [];

  const handleEntitlementDenied = (e: Event) => {
    dispatchedEvents.push((e as CustomEvent<EntitlementProblem>).detail);
  };

  beforeEach(() => {
    dispatchedEvents = [];
    window.addEventListener(ENTITLEMENT_DENIED_EVENT, handleEntitlementDenied);
    originalFetch = globalThis.fetch;
  });

  afterEach(() => {
    cleanup();
    window.removeEventListener(ENTITLEMENT_DENIED_EVENT, handleEntitlementDenied);
    globalThis.fetch = originalFetch;
    vi.restoreAllMocks();
  });

  const mockPlan403Response = (code = 'FEATURE_NOT_ENTITLED') =>
    new Response(JSON.stringify({ code, detail: 'Upgrade required' }), {
      status: 403,
      headers: { 'Content-Type': 'application/json' },
    });

  it('GET 403 plan problem does not dispatch the denial event by default', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue(mockPlan403Response());

    const { result } = renderHook(() => useAuth(), {
      wrapper: ({ children }) => <AuthProvider>{children}</AuthProvider>,
    });

    const res = await result.current.authFetch('/api/some-get');
    expect(res.status).toBe(403);
    expect(dispatchedEvents).toHaveLength(0);
  });

  it('HEAD 403 plan problem does not dispatch the denial event by default', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue(mockPlan403Response());

    const { result } = renderHook(() => useAuth(), {
      wrapper: ({ children }) => <AuthProvider>{children}</AuthProvider>,
    });

    const res = await result.current.authFetch('/api/some-head', { method: 'HEAD' });
    expect(res.status).toBe(403);
    expect(dispatchedEvents).toHaveLength(0);
  });

  it('POST 403 plan problem dispatches the denial event by default', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue(mockPlan403Response('QUOTA_EXCEEDED'));

    const { result } = renderHook(() => useAuth(), {
      wrapper: ({ children }) => <AuthProvider>{children}</AuthProvider>,
    });

    const res = await result.current.authFetch('/api/some-post', { method: 'POST' });
    expect(res.status).toBe(403);
    await new Promise((resolve) => setTimeout(resolve, 10));
    expect(dispatchedEvents).toHaveLength(1);
    expect(dispatchedEvents[0].code).toBe('QUOTA_EXCEEDED');
  });

  it('GET 403 with planDialog: true dispatches the denial event', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue(mockPlan403Response('MODULE_NOT_SUBSCRIBED'));

    const { result } = renderHook(() => useAuth(), {
      wrapper: ({ children }) => <AuthProvider>{children}</AuthProvider>,
    });

    const res = await result.current.authFetch('/api/some-explicit-get', undefined, true);
    expect(res.status).toBe(403);
    await new Promise((resolve) => setTimeout(resolve, 10));
    expect(dispatchedEvents).toHaveLength(1);
    expect(dispatchedEvents[0].code).toBe('MODULE_NOT_SUBSCRIBED');
  });

  it('GET 403 with planDialog: true in init dispatches the denial event', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue(mockPlan403Response('MODULE_NOT_SUBSCRIBED'));

    const { result } = renderHook(() => useAuth(), {
      wrapper: ({ children }) => <AuthProvider>{children}</AuthProvider>,
    });

    const res = await result.current.authFetch('/api/some-explicit-get', { planDialog: true });
    expect(res.status).toBe(403);
    await new Promise((resolve) => setTimeout(resolve, 10));
    expect(dispatchedEvents).toHaveLength(1);
    expect(dispatchedEvents[0].code).toBe('MODULE_NOT_SUBSCRIBED');
  });

  it('POST 403 with planDialog: false suppresses the denial event', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue(mockPlan403Response('QUOTA_EXCEEDED'));

    const { result } = renderHook(() => useAuth(), {
      wrapper: ({ children }) => <AuthProvider>{children}</AuthProvider>,
    });

    const res = await result.current.authFetch('/api/some-post', { method: 'POST' }, false);
    expect(res.status).toBe(403);
    expect(dispatchedEvents).toHaveLength(0);
  });

  it('withSuppressedEntitlementDenial still wins over POST or planDialog: true', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue(mockPlan403Response('QUOTA_EXCEEDED'));

    const { result } = renderHook(() => useAuth(), {
      wrapper: ({ children }) => <AuthProvider>{children}</AuthProvider>,
    });

    await withSuppressedEntitlementDenial(async () => {
      await result.current.authFetch('/api/some-post', { method: 'POST' });
      await result.current.authFetch('/api/some-get', undefined, true);
    });

    expect(dispatchedEvents).toHaveLength(0);
  });

  it('PERMISSION_DENIED 403 never dispatches entitlement-denied event', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ code: 'PERMISSION_DENIED', detail: 'Role restriction' }), {
        status: 403,
        headers: { 'Content-Type': 'application/json' },
      })
    );

    const { result } = renderHook(() => useAuth(), {
      wrapper: ({ children }) => <AuthProvider>{children}</AuthProvider>,
    });

    await result.current.authFetch('/api/some-post', { method: 'POST' });
    await result.current.authFetch('/api/some-get', undefined, true);
    expect(dispatchedEvents).toHaveLength(0);
  });
});
