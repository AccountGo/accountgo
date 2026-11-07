import React from 'react';
import { PageTabs } from './PageTabs';
import { usePermissions } from './Gating';

/** The Purchases area's sections, under each page's header: bills, and the vendors they come from. */
export const PurchasesTabs: React.FC<{ orgSlug: string }> = ({ orgSlug }) => {
  const { hasPermission } = usePermissions();
  const tabs = [
    ...(hasPermission('purchases.bills.view') ? [{ label: 'Bills', to: `/org/${orgSlug}/purchases`, end: true }] : []),
    ...(hasPermission('purchases.vendors.view') ? [{ label: 'Vendors', to: `/org/${orgSlug}/purchases/vendors`, end: false }] : []),
  ];
  if (tabs.length < 2) return null;

  return <PageTabs label="Purchases" tabs={tabs} />;
};
