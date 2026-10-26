import React from 'react';
import { NavLink } from 'react-router-dom';
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

  return (
    <nav aria-label="Products & services" className="flex gap-1 border-b border-[var(--border-subtle)]">
      {tabs.map((tab) => (
        <NavLink
          key={tab.to}
          to={tab.to}
          end={tab.end}
          className={({ isActive }) =>
            `-mb-px border-b-2 px-3 py-2 text-sm font-medium transition focus:outline-none focus-visible:ring-2 focus-visible:ring-[var(--brand-ring)] rounded-t-md ${
              isActive
                ? 'border-[var(--brand-primary)] text-[var(--text-main)]'
                : 'border-transparent text-[var(--text-secondary)] hover:text-[var(--text-main)]'
            }`
          }
        >
          {tab.label}
        </NavLink>
      ))}
    </nav>
  );
};
