import React, { useState, useEffect, useCallback } from 'react';
import { PageHeader } from '../components/PageHeader';
import { useAuth } from '../auth/AuthContext';

interface CreditNoteRow {
  id: number;
  no: string | null;
  customerName?: string;
  vendorName?: string;
  vendorCreditNo?: string;
  date: string;
  totalAmount: number;
  appliedAmount: number;
  refundedAmount: number;
  availableAmount: number;
  settlementState: string;
  statusLabel: string;
  reason: string;
}

interface AccountRolePreview {
  roleKey: string;
  roleLabel: string;
  configuredAccountCode: string | null;
  configuredAccountName: string | null;
  suggestedAccountCode: string | null;
  suggestedAccountName: string | null;
  isArchived: boolean;
  isMissing: boolean;
}

interface MappingPreview {
  readyForSalesCredits: boolean;
  readyForSupplierCredits: boolean;
  readyForAdvances: boolean;
  readyForWriteOffs: boolean;
  readyForReturns: boolean;
  roles: AccountRolePreview[];
}

export const CreditsAndSettlements: React.FC<{ side: 'sales' | 'purchasing' }> = ({ side }) => {
  const { authFetch } = useAuth();
  const [credits, setCredits] = useState<CreditNoteRow[]>([]);
  const [mappings, setMappings] = useState<MappingPreview | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const endpoint = side === 'sales' ? '/api/sales/credits' : '/api/purchasing/credits';
      const [listRes, mapRes] = await Promise.all([
        authFetch(endpoint),
        authFetch('/api/sales/credits/mappings/preview'),
      ]);
      if (listRes.ok) {
        setCredits(await listRes.json());
      } else if (listRes.status === 403) {
        setError('Credits, returns, refunds, and write-offs are not enabled for this workspace or require additional permission.');
      }
      if (mapRes.ok) {
        setMappings(await mapRes.json());
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Unable to load credits.');
    } finally {
      setLoading(false);
    }
  }, [authFetch, side]);

  useEffect(() => {
    load();
  }, [load]);

  const title = side === 'sales' ? 'Customer Credit Notes' : 'Supplier Credits & Advances';
  const subtitle =
    side === 'sales'
      ? 'Issue commercial credits, apply to open invoices, record external refunds, and manage receivable write-offs without altering original invoices.'
      : 'Record supplier credits and advances, apply across bills, and track return clearing without altering approved bills.';

  return (
    <div className="space-y-6">
      <PageHeader title={title} subtitle={subtitle} />

      {error && (
        <div className="rounded-lg border border-amber-200 bg-amber-50 p-4 text-sm text-amber-900">
          {error}
        </div>
      )}

      {mappings && (
        <div className="rounded-lg border border-slate-200 bg-white p-4 shadow-sm">
          <h2 className="text-sm font-semibold text-slate-900">Control-Account Mapping Readiness</h2>
          <p className="mt-1 text-xs text-slate-500">
            Accounts are snapshotted at posting. Missing or archived mappings block posting until reviewed.
          </p>
          <div className="mt-3 grid grid-cols-1 gap-2 sm:grid-cols-2 lg:grid-cols-3">
            {mappings.roles.map((r) => (
              <div key={r.roleKey} className="rounded border border-slate-100 bg-slate-50 p-2 text-xs">
                <div className="font-medium text-slate-800">{r.roleLabel}</div>
                {r.configuredAccountCode ? (
                  <div className="text-emerald-700">
                    {r.configuredAccountCode} — {r.configuredAccountName}
                    {r.isArchived ? ' (Archived)' : ''}
                  </div>
                ) : (
                  <div className="text-amber-700">
                    Unconfigured{r.suggestedAccountCode ? ` (Suggested: ${r.suggestedAccountCode} ${r.suggestedAccountName})` : ''}
                  </div>
                )}
              </div>
            ))}
          </div>
        </div>
      )}

      <div className="rounded-lg border border-slate-200 bg-white shadow-sm overflow-x-auto">
        <table className="min-w-full divide-y divide-slate-200 text-sm">
          <thead className="bg-slate-50">
            <tr>
              <th className="px-4 py-3 text-left font-medium text-slate-600">Number</th>
              <th className="px-4 py-3 text-left font-medium text-slate-600">{side === 'sales' ? 'Customer' : 'Vendor'}</th>
              <th className="px-4 py-3 text-left font-medium text-slate-600">Date</th>
              <th className="px-4 py-3 text-left font-medium text-slate-600">Status</th>
              <th className="px-4 py-3 text-right font-medium text-slate-600">Total</th>
              <th className="px-4 py-3 text-right font-medium text-slate-600">Applied</th>
              <th className="px-4 py-3 text-right font-medium text-slate-600">Refunded</th>
              <th className="px-4 py-3 text-right font-medium text-slate-600">Available</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {loading ? (
              <tr>
                <td colSpan={8} className="px-4 py-6 text-center text-slate-500">
                  Loading...
                </td>
              </tr>
            ) : credits.length === 0 ? (
              <tr>
                <td colSpan={8} className="px-4 py-6 text-center text-slate-500">
                  No credit notes recorded yet.
                </td>
              </tr>
            ) : (
              credits.map((c) => (
                <tr key={c.id} className="hover:bg-slate-50">
                  <td className="px-4 py-3 font-medium text-slate-900">{c.no ?? `Draft #${c.id}`}</td>
                  <td className="px-4 py-3 text-slate-700">{c.customerName ?? c.vendorName ?? ''}</td>
                  <td className="px-4 py-3 text-slate-600">{c.date.slice(0, 10)}</td>
                  <td className="px-4 py-3 text-slate-700">{c.settlementState}</td>
                  <td className="px-4 py-3 text-right font-mono">{c.totalAmount.toFixed(2)}</td>
                  <td className="px-4 py-3 text-right font-mono">{c.appliedAmount.toFixed(2)}</td>
                  <td className="px-4 py-3 text-right font-mono">{c.refundedAmount.toFixed(2)}</td>
                  <td className="px-4 py-3 text-right font-mono font-semibold">{c.availableAmount.toFixed(2)}</td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
};
