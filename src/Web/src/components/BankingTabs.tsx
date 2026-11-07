import React from 'react';
import { PageTabs } from './PageTabs';
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

  return <PageTabs label="Banking" tabs={tabs.map((tab) => ({ ...tab, end: true, active: tab.id === active }))} />;
};

export default BankingTabs;
