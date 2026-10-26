import React from 'react';
import { NavLink } from 'react-router-dom';
import { usePermissions } from './Gating';

/** The Sales area's sections, under each page's header: invoices, and the clients they are billed to. */
export const SalesTabs: React.FC<{ orgSlug: string }> = ({ orgSlug }) => {
  const { hasPermission } = usePermissions();
  const tabs = [
    { label: 'Invoices', to: `/org/${orgSlug}/sales`, end: true },
    ...(hasPermission('sales.customers.view') ? [{ label: 'Clients', to: `/org/${orgSlug}/sales/clients`, end: false }] : []),
  ];
  if (tabs.length < 2) return null;

  return (
    <nav aria-label="Sales" className="flex gap-1 border-b border-[var(--border-subtle)]">
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
