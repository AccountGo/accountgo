import React from 'react';
import { PageTabs } from './PageTabs';
import { usePermissions, useTenantContext } from './Gating';

/** The Products & services area's sections, under each page's header: the item catalog, and the stock held of it. */
export const ItemsTabs: React.FC<{ orgSlug: string }> = ({ orgSlug }) => {
  const { hasModule } = useTenantContext();
  const { hasPermission } = usePermissions();
  const tabs = [
    { label: 'Items', to: `/org/${orgSlug}/items`, end: true },
    ...(hasModule('inventory') && hasPermission('inventory.items.view')
      ? [{ label: 'Inventory', to: `/org/${orgSlug}/items/inventory`, end: false }]
      : []),
  ];
  if (tabs.length < 2) return null;

  return <PageTabs label="Products & services" tabs={tabs} />;
};
