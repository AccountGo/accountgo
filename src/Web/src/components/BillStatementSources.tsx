import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { usePermissions } from './Gating';

export function BillStatementSources({ billId, orgSlug }: { billId: number; orgSlug: string }) {
  const { authFetch } = useAuth();
  const { hasPermission } = usePermissions();
  const canView = hasPermission('banking.accounts.view');
  const [sources, setSources] = useState<{ id: number; bankId: number }[]>([]);
  useEffect(() => {
    let active = true;
    setSources([]);
    if (!canView) return;
    void authFetch(`/api/Banking/StatementLines/ForBill/${billId}`).then(async res => {
      if (res.ok) { const data = await res.json(); if (active) setSources(data); }
    }).catch(() => {});
    return () => { active = false; };
  }, [authFetch, billId, orgSlug, canView]);
  return sources.length ? <section className="text-sm space-y-1">
    <h2 className="font-semibold">Source statement transactions</h2>
    {sources.map(s => <Link className="block underline" key={s.id}
      to={`/org/${orgSlug}/banking/${s.bankId}/statement-import?lineId=${s.id}&status=all`}>Review source transaction {s.id}</Link>)}
  </section> : null;
}
