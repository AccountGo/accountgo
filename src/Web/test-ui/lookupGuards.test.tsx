// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { BillStatementSources } from '../src/components/BillStatementSources';
import { InvoiceEdit } from '../src/pages/InvoiceEdit';
import { RecurringScheduleEdit } from '../src/pages/RecurringScheduleEdit';

const { authFetch, hasPermission, hasModule, caps, outlet } = vi.hoisted(() => ({
  authFetch: vi.fn(),
  hasPermission: vi.fn((_p: string) => true),
  hasModule: vi.fn((_m: string) => true),
  caps: {
    canStatementImport: true,
    ready: true,
  },
  outlet: {
    currentOrg: { id: '1', slug: 'acme', plan: 'invoicing' },
    subscription: { planId: 'invoicing' },
    hasModule: (m: string) => hasModule(m),
    canWrite: () => true,
  },
}));

vi.mock('../src/auth/AuthContext', () => ({
  useAuth: () => ({ authFetch }),
}));

vi.mock('../src/components/Gating', () => ({
  usePermissions: () => ({ hasPermission }),
  ReadOnlyModuleBanner: () => null,
}));

vi.mock('../src/lib/entitlements', () => ({
  useOptionalCapabilities: () => caps,
}));

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return {
    ...actual,
    useOutletContext: () => outlet,
  };
});

describe('Lookup guards for unentitled features', () => {
  beforeEach(() => {
    cleanup();
    authFetch.mockReset();
    hasPermission.mockImplementation(() => true);
    hasModule.mockImplementation(() => true);
    caps.canStatementImport = true;
    caps.ready = true;

    authFetch.mockImplementation(async (url: string) => {
      if (url.includes('/api/Tax/Taxes')) {
        return new Response(JSON.stringify({ taxes: [] }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        });
      }
      return new Response(JSON.stringify([]), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      });
    });
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  describe('BillStatementSources', () => {
    it('omits StatementLines/ForBill call when canStatementImport is false', async () => {
      caps.canStatementImport = false;
      render(
        <MemoryRouter>
          <BillStatementSources billId={42} orgSlug="acme" />
        </MemoryRouter>
      );

      await waitFor(() => {
        expect(authFetch).not.toHaveBeenCalledWith(expect.stringContaining('/ForBill/42'));
      });
    });

    it('omits StatementLines/ForBill call when user lacks banking.accounts.view permission', async () => {
      caps.canStatementImport = true;
      hasPermission.mockImplementation((p: string) => p !== 'banking.accounts.view');
      render(
        <MemoryRouter>
          <BillStatementSources billId={42} orgSlug="acme" />
        </MemoryRouter>
      );

      await waitFor(() => {
        expect(authFetch).not.toHaveBeenCalledWith(expect.stringContaining('/ForBill/42'));
      });
    });

    it('fetches StatementLines/ForBill when both canStatementImport and view permission are true', async () => {
      caps.canStatementImport = true;
      hasPermission.mockReturnValue(true);
      render(
        <MemoryRouter>
          <BillStatementSources billId={42} orgSlug="acme" />
        </MemoryRouter>
      );

      await waitFor(() => {
        expect(authFetch).toHaveBeenCalledWith('/api/Banking/StatementLines/ForBill/42');
      });
    });
  });

  describe('InvoiceEdit Common/Items lookup guard', () => {
    it('omits Common/Items call when inventory module is absent', async () => {
      hasModule.mockImplementation((m: string) => m !== 'inventory');
      hasPermission.mockReturnValue(true);

      render(
        <MemoryRouter initialEntries={['/org/acme/invoices/new']}>
          <Routes>
            <Route path="/org/:orgSlug/invoices/new" element={<InvoiceEdit />} />
          </Routes>
        </MemoryRouter>
      );

      await waitFor(() => {
        expect(authFetch).toHaveBeenCalledWith('/api/Sales/Customers');
      });
      expect(authFetch).not.toHaveBeenCalledWith('/api/Common/Items');
    });

    it('omits Common/Items call when lacking inventory.items.view permission', async () => {
      hasModule.mockReturnValue(true);
      hasPermission.mockImplementation((p: string) => p !== 'inventory.items.view');

      render(
        <MemoryRouter initialEntries={['/org/acme/invoices/new']}>
          <Routes>
            <Route path="/org/:orgSlug/invoices/new" element={<InvoiceEdit />} />
          </Routes>
        </MemoryRouter>
      );

      await waitFor(() => {
        expect(authFetch).toHaveBeenCalledWith('/api/Sales/Customers');
      });
      expect(authFetch).not.toHaveBeenCalledWith('/api/Common/Items');
    });

    it('calls Common/Items when inventory module and permission are present', async () => {
      hasModule.mockReturnValue(true);
      hasPermission.mockReturnValue(true);

      render(
        <MemoryRouter initialEntries={['/org/acme/invoices/new']}>
          <Routes>
            <Route path="/org/:orgSlug/invoices/new" element={<InvoiceEdit />} />
          </Routes>
        </MemoryRouter>
      );

      await waitFor(() => {
        expect(authFetch).toHaveBeenCalledWith('/api/Common/Items');
      });
    });
  });

  describe('RecurringScheduleEdit Common/Items lookup guard', () => {
    it('omits Common/Items call when inventory module is absent', async () => {
      hasModule.mockImplementation((m: string) => m !== 'inventory');
      hasPermission.mockReturnValue(true);

      render(
        <MemoryRouter initialEntries={['/org/acme/schedules/new']}>
          <Routes>
            <Route path="/org/:orgSlug/schedules/new" element={<RecurringScheduleEdit />} />
          </Routes>
        </MemoryRouter>
      );

      await waitFor(() => {
        expect(authFetch).toHaveBeenCalledWith('/api/Sales/Customers');
      });
      expect(authFetch).not.toHaveBeenCalledWith('/api/Common/Items');
    });

    it('calls Common/Items when inventory module and permission are present', async () => {
      hasModule.mockReturnValue(true);
      hasPermission.mockReturnValue(true);

      render(
        <MemoryRouter initialEntries={['/org/acme/schedules/new']}>
          <Routes>
            <Route path="/org/:orgSlug/schedules/new" element={<RecurringScheduleEdit />} />
          </Routes>
        </MemoryRouter>
      );

      await waitFor(() => {
        expect(authFetch).toHaveBeenCalledWith('/api/Common/Items');
      });
    });
  });
});
