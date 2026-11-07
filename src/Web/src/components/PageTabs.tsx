import React from 'react';
import { NavLink } from 'react-router-dom';

export interface PageTab {
  label: string;
  to: string;
  end?: boolean;
  active?: boolean;
}

/** Consistent section navigation beneath a page header. */
export const PageTabs: React.FC<{ label: string; tabs: PageTab[] }> = ({ label, tabs }) => (
  <nav aria-label={label} className="flex flex-wrap gap-1 border-b border-[var(--border-subtle)]">
    {tabs.map((tab) => (
      <NavLink
        key={tab.to}
        to={tab.to}
        end={tab.end}
        aria-current={tab.active ? 'page' : undefined}
        className={({ isActive }) =>
          `-mb-px shrink-0 border-b-2 px-3 py-2 text-sm font-medium transition focus:outline-none focus-visible:ring-2 focus-visible:ring-[var(--brand-ring)] rounded-t-md ${
            (tab.active ?? isActive)
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
