import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import React, { useCallback, useState } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { evaluateHasPermission } from '../src/layouts/OrgLayout';
import { announcePermissionDenial } from '../src/lib/permissionDenialEvents';
import { refreshWorkspaceAccess, resetPermissionRefresh, throttledRefresh } from '../src/lib/permissionRefresh';
import { useActionDenialRefresh } from '../src/lib/useActionDenialRefresh';

const { server } = vi.hoisted(() => ({ server: { permissions: [] as string[], reads: 0 } }));

const denied = () =>
  new Response(JSON.stringify({ title: 'Access Denied', status: 403, code: 'PERMISSION_DENIED', requiredPermission: 'purchases.bills.post' }), {
    status: 403,
    headers: { 'Content-Type': 'application/problem+json' },
  });

// A workspace shell in miniature: permissions held in state, the real shared refresh, and the real listener hook.
const Shell: React.FC<{ children: (permissions: string[]) => React.ReactNode }> = ({ children }) => {
  const [permissions, setPermissions] = useState<string[]>(['purchases.bills.view', 'purchases.bills.post']);
  const refreshPermissions = useCallback(
    (options?: { force?: boolean }) =>
      throttledRefresh('acme\u0000me', () => refreshWorkspaceAccess({
        fetchCurrent: async () => { server.reads += 1; return new Response(JSON.stringify({ slug: 'acme', role: 'Member', permissions: server.permissions }), { status: 200 }); },
        getCurrent: () => ({ slug: 'acme', role: 'Member', permissions }),
        apply: (updates) => setPermissions(updates.permissions),
      }), { force: options?.force }),
    [permissions],
  );
  useActionDenialRefresh(refreshPermissions);
  return <>{children(permissions)}</>;
};

// The button is still visible (the member's screen was loaded before the role was taken away); the server refuses the click.
const BillActions: React.FC<{ permissions: string[] }> = ({ permissions }) => (
  <div>
    {evaluateHasPermission(permissions, 'purchases.bills.post') && (
      <button type="button" onClick={() => void announcePermissionDenial(denied(), { action: true })}>Approve bill</button>
    )}
  </div>
);

beforeEach(() => { resetPermissionRefresh(); server.reads = 0; server.permissions = ['purchases.bills.view']; });
afterEach(() => { cleanup(); vi.clearAllMocks(); });

describe('refreshing permissions after a server denial', () => {
  it('re-reads once after a denied action, and the control the member no longer holds disappears', async () => {
    render(<Shell>{(permissions) => <BillActions permissions={permissions} />}</Shell>);

    fireEvent.click(screen.getByRole('button', { name: 'Approve bill' }));

    await waitFor(() => expect(screen.queryByRole('button', { name: 'Approve bill' })).toBeNull());
    expect(server.reads).toBe(1);
  });

  it('does not read again for a second denial inside the minute', async () => {
    server.permissions = ['purchases.bills.view', 'purchases.bills.post']; // the server still allows it: a stricter check than the client
    render(<Shell>{(permissions) => <BillActions permissions={permissions} />}</Shell>);

    fireEvent.click(screen.getByRole('button', { name: 'Approve bill' }));
    await waitFor(() => expect(server.reads).toBe(1));
    fireEvent.click(screen.getByRole('button', { name: 'Approve bill' }));
    await act(async () => { await new Promise((resolve) => setTimeout(resolve, 30)); });

    expect(server.reads).toBe(1);
    expect(screen.getByRole('button', { name: 'Approve bill' })).toBeTruthy(); // still allowed, nothing loops
  });

  it('shares one read between denials that arrive together', async () => {
    render(<Shell>{(permissions) => <BillActions permissions={permissions} />}</Shell>);

    await act(async () => {
      await Promise.all([announcePermissionDenial(denied(), { action: true }), announcePermissionDenial(denied(), { action: false })]);
    });
    await waitFor(() => expect(server.reads).toBe(1));
  });

  it('a denied read refreshes too, because its page already explains it but the permissions may be stale', async () => {
    render(<Shell>{(permissions) => <BillActions permissions={permissions} />}</Shell>);

    await act(async () => { await announcePermissionDenial(denied(), { action: false }); });

    await waitFor(() => expect(server.reads).toBe(1));
  });

  it('is not forced: the one-minute limit from an earlier refresh still applies', async () => {
    render(<Shell>{(permissions) => <BillActions permissions={permissions} />}</Shell>);
    await act(async () => { await throttledRefresh('acme\u0000me', async () => false); }); // a refresh just ran (for example the route guard's)

    await act(async () => { await announcePermissionDenial(denied(), { action: true }); });
    await act(async () => { await new Promise((resolve) => setTimeout(resolve, 30)); });

    expect(server.reads).toBe(0);
  });
});
