import React from 'react';
import { NavLink } from 'react-router-dom';
import {
  LayoutDashboard,
  BookOpen,
  Receipt,
  Settings,
  Menu,
} from 'lucide-react';
import { ModuleKey, OrgDetails } from '../types';

interface MobileBottomNavProps {
  currentOrg: OrgDetails;
  onOpenMenu: () => void;
  hasPermission: (permission: string | string[]) => boolean;
}

export const MobileBottomNav: React.FC<MobileBottomNavProps> = ({
  currentOrg,
  onOpenMenu,
  hasPermission,
}) => {
  const activeModules = currentOrg.subscription?.activeModules || [];

  const allNavItems: Array<{
    name: string;
    path: string;
    icon: typeof LayoutDashboard;
    module?: ModuleKey;
    permission?: string;
    adminOnly?: boolean;
  }> = [
    {
      name: 'Dashboard',
      path: `/org/${currentOrg.slug}/dashboard`,
      icon: LayoutDashboard,
      module: 'financials',
      permission: 'sales.invoices.view',
    },
    {
      name: 'Sales',
      path: `/org/${currentOrg.slug}/sales`,
      icon: Receipt,
      module: 'sales',
      permission: 'sales.invoices.view',
    },
    {
      name: 'Ledger',
      path: `/org/${currentOrg.slug}/financials`,
      icon: BookOpen,
      module: 'financials',
      permission: 'financials.accounts.view',
    },
    {
      name: 'Settings',
      path: `/org/${currentOrg.slug}/settings`,
      icon: Settings,
      adminOnly: true,
    },
  ];

  const navItems = allNavItems.filter((item) =>
    (!item.module || activeModules.includes(item.module)) &&
    (!item.permission || hasPermission(item.permission)) &&
    (!item.adminOnly || currentOrg.role === 'Owner' || currentOrg.role === 'Admin')
  );

  return (
    <nav
      className="no-print md:hidden fixed bottom-0 left-0 right-0 z-30 bg-[var(--bg-header)] backdrop-blur-2xl px-2 safe-bottom flex items-center justify-around h-16 shadow-[0_-8px_30px_rgba(0,0,0,0.4)] transition-colors duration-200"
      aria-label="Mobile Bottom Navigation"
    >
      {navItems.map((item) => {
        const Icon = item.icon;
        return (
          <NavLink
            key={item.path}
            to={item.path}
            className={({ isActive }) =>
              `flex flex-col items-center justify-center flex-1 h-full py-1 text-[10px] font-medium transition-all active:scale-95 ${
                isActive
                  ? 'text-[var(--brand-primary)] font-semibold'
                  : 'text-[var(--text-muted)] hover:text-[var(--text-main)]'
              }`
            }
          >
            {({ isActive }) => (
              <>
                <div
                  className={`p-1.5 rounded-xl transition-colors ${
                    isActive ? 'bg-[var(--brand-subtle)]' : 'bg-transparent'
                  }`}
                >
                  <Icon className="w-5 h-5" />
                </div>
                <span className="truncate mt-0.5">{item.name}</span>
              </>
            )}
          </NavLink>
        );
      })}

      {/* Menu / Drawer Toggle */}
      <button
        type="button"
        onClick={onOpenMenu}
        aria-label="Open navigation menu"
        className="flex flex-col items-center justify-center flex-1 h-full py-1 text-[10px] font-medium text-[var(--text-muted)] hover:text-[var(--text-main)] transition-all active:scale-95 cursor-pointer"
      >
        <div className="p-1.5 rounded-xl">
          <Menu className="w-5 h-5" />
        </div>
        <span className="truncate mt-0.5">More</span>
      </button>
    </nav>
  );
};

export default MobileBottomNav;
