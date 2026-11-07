import React from 'react';
import { PageTabs } from './PageTabs';
import { usePermissions } from './Gating';

/** The Sales area's sections, under each page's header: invoices, and the clients they are billed to. */
export const SalesTabs: React.FC<{ orgSlug: string }> = ({ orgSlug }) => {
  const { hasPermission } = usePermissions();
  const tabs = [
    { label: 'Invoices', to: `/org/${orgSlug}/sales`, end: true },
    ...(hasPermission('sales.invoices.view') ? [{ label: 'Recurring', to: `/org/${orgSlug}/sales/recurring`, end: false }] : []),
    ...(hasPermission('sales.customers.view') ? [{ label: 'Clients', to: `/org/${orgSlug}/sales/clients`, end: false }] : []),
  ];
  if (tabs.length < 2) return null;

  return <PageTabs label="Sales" tabs={tabs} />;
};
