import React from 'react';
import { NavLink, Link } from 'react-router-dom';
import {
  LayoutDashboard,
  BookOpen,
  Receipt,
  ShoppingBag,
  Package,
  BarChart3,
  Settings,
  Users,
  X,
  Sparkles,
} from 'lucide-react';
import { OrgDetails, UserProfile, SidebarMode, SubscriptionInfo, ModuleKey } from '../types';
import { OrgSwitcher } from './OrgSwitcher';
import { UserProfileSection } from './UserProfileSection';

interface SidebarProps {
  currentOrg: OrgDetails;
  availableOrgs: OrgDetails[];
  user: UserProfile;
  sidebarMode?: SidebarMode;
  isMobileOpen?: boolean;
  onMobileClose?: () => void;
  subscription?: SubscriptionInfo;
  hasModule?: (module: ModuleKey) => boolean;
  hasPermission?: (permission: string | string[]) => boolean;
}

export const Sidebar: React.FC<SidebarProps> = ({
  currentOrg,
  availableOrgs,
  user,
  sidebarMode = 'expanded',
  isMobileOpen = false,
  onMobileClose,
  subscription,
  hasModule,
  hasPermission,
}) => {
  const isCollapsed = sidebarMode === 'collapsed';

  const mainLinks: Array<{
    name: string;
    path: string;
    icon: React.ComponentType<{ className?: string }>;
    /** With several modules, any one of them shows the link. */
    module: ModuleKey | ModuleKey[];
    permission?: string;
    badge?: string;
  }> = [
    {
      name: 'Dashboard',
      path: `/org/${currentOrg.slug}/dashboard`,
      icon: LayoutDashboard,
      module: 'financials',
    },
    {
      name: 'Invoices & Sales',
      path: `/org/${currentOrg.slug}/sales`,
      icon: Receipt,
      module: 'sales',
      permission: 'sales.invoices.view',
    },
    {
      name: 'Purchases & Bills',
      path: `/org/${currentOrg.slug}/purchases`,
      icon: ShoppingBag,
      module: 'purchases',
      permission: 'purchases.bills.view',
    },
    {
      // Items are shared by sales and purchases, so they live here rather than under either.
      name: 'Products & services',
      path: `/org/${currentOrg.slug}/items`,
      icon: Package,
      module: ['sales', 'purchases'],
      permission: 'inventory.items.view',
    },
    {
      name: 'General Ledger',
      path: `/org/${currentOrg.slug}/financials`,
      icon: BookOpen,
      module: 'financials',
      permission: 'financials.journal.view',
    },
    {
      name: 'Reports',
      path: `/org/${currentOrg.slug}/reports`,
      icon: BarChart3,
      module: 'reports',
      permission: 'reports.view',
    },
  ];

  const visibleMainLinks = mainLinks.filter((link) => {
    if (hasModule && !(Array.isArray(link.module) ? link.module : [link.module]).some(hasModule)) return false;
    if (hasPermission && link.permission && !hasPermission(link.permission)) return false;
    return true;
  });

  const adminLinks = [
    {
      name: 'Team & Members',
      path: `/org/${currentOrg.slug}/members`,
      icon: Users,
      visible: true,
    },
    {
      name: 'Organization Settings',
      path: `/org/${currentOrg.slug}/settings`,
      icon: Settings,
      visible: currentOrg.role === 'Owner' || currentOrg.role === 'Admin',
    },
  ].filter((link) => link.visible);

  const renderContent = (isMobile: boolean = false) => (
    <aside
      className={`no-print h-full flex flex-col bg-[var(--bg-sidebar)] backdrop-blur-2xl transition-[width] duration-300 ease-in-out select-none text-[var(--text-main)] ${
        isMobile ? 'w-72 safe-top safe-bottom shadow-2xl shadow-black/80' : isCollapsed ? 'w-20' : 'w-64'
      }`}
    >
      {/* Brand Header */}
      <div
        className={`h-16 flex items-center flex-shrink-0 transition-all ${
          isCollapsed && !isMobile
            ? 'justify-center px-2'
            : 'justify-between px-4'
        }`}
      >
        <Link
          to={`/org/${currentOrg.slug}/dashboard`}
          onClick={isMobile ? onMobileClose : undefined}
          className={`flex items-center gap-3 group focus:outline-none min-w-0 ${
            isCollapsed && !isMobile ? 'justify-center' : ''
          }`}
          title={isCollapsed && !isMobile ? (currentOrg.whitelabel?.brandName || 'AccountGo') : undefined}
        >
          <img
            src={currentOrg.whitelabel?.logoUrl || '/accountgo-mark.svg'}
            alt={currentOrg.whitelabel?.brandName || 'AccountGo'}
            className="w-8 h-8 rounded-lg shadow-md group-hover:scale-105 transition-transform shrink-0"
          />
          {(!isCollapsed || isMobile) && (
            <span className="font-bold text-base tracking-tight text-[var(--text-main)] truncate">
              {currentOrg.whitelabel?.brandName ? (
                currentOrg.whitelabel.brandName
              ) : (
                <>
                  Account<span className="text-[var(--brand-primary)]">Go</span>
                </>
              )}
            </span>
          )}
        </Link>

        {/* Mobile Close Button */}
        {isMobile && (
          <button
            type="button"
            onClick={onMobileClose}
            className="p-2 rounded-xl text-[var(--text-muted)] hover:text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)] active:scale-95 transition-all"
            aria-label="Close navigation drawer"
          >
            <X className="w-5 h-5" />
          </button>
        )}
      </div>

      {/* Organization Switcher */}
      <div className="px-3 py-2 flex-shrink-0 relative z-30">
        <OrgSwitcher
          currentOrg={currentOrg}
          availableOrgs={availableOrgs}
          collapsed={isMobile ? false : isCollapsed}
        />
      </div>

      {/* Navigation Sections */}
      <div className="flex-1 overflow-y-auto px-3 py-3 space-y-6 smooth-scroll">
        {/* Main Section */}
        <div>
          {(!isCollapsed || isMobile) && (
            <div className="px-3 mb-2 text-[10px] font-bold uppercase tracking-wider text-[var(--text-muted)]">
              Workspace
            </div>
          )}
          <nav aria-label="Workspace navigation" className="space-y-1">
            {visibleMainLinks.map((link) => {
              const Icon = link.icon;
              return (
                <NavLink
                  key={link.path}
                  to={link.path}
                  onClick={isMobile ? onMobileClose : undefined}
                  className={({ isActive }) =>
                    `flex items-center gap-3 px-3 py-2.5 rounded-xl text-sm font-medium transition-all group min-h-[44px] active:scale-[0.98] ${
                      isActive
                        ? 'bg-[var(--brand-subtle)] text-[var(--brand-primary)] font-semibold shadow-sm'
                        : 'text-[var(--text-secondary)] hover:text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)]'
                    } ${isCollapsed && !isMobile ? 'justify-center px-2' : ''}`
                  }
                  title={isCollapsed && !isMobile ? link.name : undefined}
                >
                  {({ isActive }) => (
                    <>
                      <Icon className="w-4 h-4 flex-shrink-0 transition-transform group-hover:scale-110" />
                      <span className={isCollapsed && !isMobile ? 'sr-only' : 'flex-1 truncate'}>
                        {link.name}
                      </span>
                      {(!isCollapsed || isMobile) && link.badge && (
                        <span className="text-[10px] px-2 py-0.5 rounded-full bg-[var(--bg-subtle)] text-[var(--text-muted)] font-mono">
                          {link.badge}
                        </span>
                      )}
                      {isActive && <span className="sr-only">(current page)</span>}
                    </>
                  )}
                </NavLink>
              );
            })}
          </nav>

          {/* Invoicing Plan Upgrade Teaser */}
          {(!isCollapsed || isMobile) && subscription?.planId === 'invoicing' && (
            <div className="mt-4 p-3 rounded-2xl bg-[var(--brand-subtle)] text-[var(--text-main)] border border-transparent">
              <div className="flex items-center gap-1.5 text-[11px] font-bold uppercase tracking-wider text-[var(--brand-primary)] mb-1">
                <Sparkles className="w-3.5 h-3.5" />
                Invoicing Plan
              </div>
              <p className="text-xs text-[var(--text-secondary)] leading-relaxed mb-2">
                Need general ledger, bank feeds, or bill payments?
              </p>
              <Link
                to={`/org/${currentOrg.slug}/settings?tab=subscription`}
                onClick={isMobile ? onMobileClose : undefined}
                className="inline-flex items-center gap-1 text-xs font-semibold text-[var(--brand-primary)] hover:underline"
              >
                Upgrade to Accounting &rarr;
              </Link>
            </div>
          )}
        </div>

        {/* Administration Section */}
        <div>
          {(!isCollapsed || isMobile) && (
            <div className="px-3 mb-2 text-[10px] font-bold uppercase tracking-wider text-[var(--text-muted)]">
              Administration
            </div>
          )}
          <nav aria-label="Administration navigation" className="space-y-1">
            {adminLinks.map((link) => {
              const Icon = link.icon;
              return (
                <NavLink
                  key={link.path}
                  to={link.path}
                  onClick={isMobile ? onMobileClose : undefined}
                  className={({ isActive }) =>
                    `flex items-center gap-3 px-3 py-2.5 rounded-xl text-sm font-medium transition-all group min-h-[44px] active:scale-[0.98] ${
                      isActive
                        ? 'bg-[var(--brand-subtle)] text-[var(--brand-primary)] font-semibold shadow-sm'
                        : 'text-[var(--text-secondary)] hover:text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)]'
                    } ${isCollapsed && !isMobile ? 'justify-center px-2' : ''}`
                  }
                  title={isCollapsed && !isMobile ? link.name : undefined}
                >
                  {({ isActive }) => (
                    <>
                      <Icon className="w-4 h-4 flex-shrink-0 transition-transform group-hover:scale-110" />
                      <span className={isCollapsed && !isMobile ? 'sr-only' : 'flex-1 truncate'}>
                        {link.name}
                      </span>
                      {isActive && <span className="sr-only">(current page)</span>}
                    </>
                  )}
                </NavLink>
              );
            })}
          </nav>
        </div>
      </div>

      {/* Footer: User Profile Section */}
      <div className="p-3 flex-shrink-0 relative z-30">
        <UserProfileSection
          user={user}
          collapsed={isMobile ? false : isCollapsed}
          currentOrgSlug={currentOrg.slug}
          onNavigate={isMobile ? onMobileClose : undefined}
        />
      </div>
    </aside>
  );

  return (
    <>
      {/* Desktop Sidebar */}
      <div
        className={`hidden md:block h-full transition-[width,opacity] duration-300 ease-in-out flex-shrink-0 ${
          sidebarMode === 'closed'
            ? 'w-0 opacity-0 pointer-events-none overflow-hidden'
            : isCollapsed
            ? 'w-20 opacity-100 overflow-visible relative z-40'
            : 'w-64 opacity-100 overflow-visible relative z-40'
        }`}
      >
        {renderContent(false)}
      </div>

      {/* Mobile Drawer Backdrop & Animated Panel */}
      <div
        className={`fixed inset-0 z-50 md:hidden transition-all duration-300 ${
          isMobileOpen
            ? 'opacity-100 pointer-events-auto bg-black/60 backdrop-blur-sm'
            : 'opacity-0 pointer-events-none'
        }`}
        onClick={onMobileClose}
      >
        <div
          className={`h-full max-w-[85vw] transition-transform duration-300 ease-out ${
            isMobileOpen ? 'translate-x-0' : '-translate-x-full'
          }`}
          onClick={(e) => e.stopPropagation()}
        >
          {renderContent(true)}
        </div>
      </div>
    </>
  );
};

export default Sidebar;
