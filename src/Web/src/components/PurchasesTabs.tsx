import React from 'react';
import { NavLink } from 'react-router-dom';
import { usePermissions } from './Gating';

/** The Purchases area's sections, under each page's header: bills, and the vendors they come from. */
export const PurchasesTabs: React.FC<{ orgSlug: string }> = ({ orgSlug }) => {
  const { hasPermission } = usePermissions();
  const tabs = [
    ...(hasPermission('purchases.bills.view') ? [{ label: 'Bills', to: `/org/${orgSlug}/purchases`, end: true }] : []),
    ...(hasPermission('purchases.vendors.view') ? [{ label: 'Vendors', to: `/org/${orgSlug}/purchases/vendors`, end: false }] : []),
  ];
  if (tabs.length < 2) return null;

  return (
    <nav aria-label="Purchases" className="flex gap-1 border-b border-[var(--border-subtle)]">
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
