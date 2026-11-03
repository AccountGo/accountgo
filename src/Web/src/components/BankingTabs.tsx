import React from 'react';
import { Link } from 'react-router-dom';
import { useTenantContext } from './Gating';
import { useIntegrations } from '../lib/integrations';

type BankingTab = 'accounts' | 'transactions' | 'reconcile' | 'review';

interface BankingTabsProps {
  active: BankingTab;
  orgSlug: string;
}

export const BankingTabs: React.FC<BankingTabsProps> = ({ active, orgSlug }) => {
  const { hasModule } = useTenantContext();
  const { capabilities, summary } = useIntegrations();

  const tabs: { id: BankingTab; label: string; to: string }[] = [
    { id: 'accounts', label: 'Accounts', to: `/org/${orgSlug}/banking` },
    ...(capabilities?.canReview ? [{ id: 'review' as const, label: `Needs review${summary ? ` (${summary.pendingCount})` : ''}`, to: `/org/${orgSlug}/banking/needs-review` }] : []),
    ...(hasModule('financials') ? [
      { id: 'transactions' as const, label: 'Transactions', to: `/org/${orgSlug}/banking/transactions` },
      { id: 'reconcile' as const, label: 'Reconcile', to: `/org/${orgSlug}/banking/reconcile` },
    ] : []),
  ];

  return (
    <div className="flex flex-wrap gap-1 border-b border-[var(--border-subtle)] pb-px">
      {tabs.map((tab) => {
        const isActive = tab.id === active;
        return (
          <Link
            key={tab.id}
            to={tab.to}
            className={`h-9 px-4 rounded-t-xl text-xs font-semibold transition ${
              isActive
                ? 'bg-[var(--bg-surface)] text-[var(--text-main)] border border-b-0 border-[var(--border-subtle)]'
                : 'text-[var(--text-muted)] hover:text-[var(--text-main)]'
            }`}
          >
            {tab.label}
          </Link>
        );
      })}
    </div>
  );
};

export default BankingTabs;
