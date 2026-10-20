import React from 'react';
import { NavLink, Link } from 'react-router-dom';
import {
  LayoutDashboard,
  BookOpen,
  Receipt,
  ShoppingBag,
  BarChart3,
  Settings,
  Users,
  X,
} from 'lucide-react';
import { OrgDetails, UserProfile, SidebarMode } from '../types';
import { OrgSwitcher } from './OrgSwitcher';
import { UserProfileSection } from './UserProfileSection';

interface SidebarProps {
  currentOrg: OrgDetails;
  availableOrgs: OrgDetails[];
  user: UserProfile;
  sidebarMode?: SidebarMode;
  isMobileOpen?: boolean;
  onMobileClose?: () => void;
}

export const Sidebar: React.FC<SidebarProps> = ({
  currentOrg,
  availableOrgs,
  user,
  sidebarMode = 'expanded',
  isMobileOpen = false,
  onMobileClose,
}) => {
  const isCollapsed = sidebarMode === 'collapsed';

  const mainLinks = [
    {
      name: 'Dashboard',
      path: `/org/${currentOrg.slug}/dashboard`,
      icon: LayoutDashboard,
    },
    {
      name: 'Financials',
      path: `/org/${currentOrg.slug}/financials`,
      icon: BookOpen,
      badge: 'Ledger',
    },
    {
      name: 'Sales & Invoices',
      path: `/org/${currentOrg.slug}/sales`,
      icon: Receipt,
    },
    {
      name: 'Purchases & Bills',
      path: `/org/${currentOrg.slug}/purchases`,
      icon: ShoppingBag,
    },
    {
      name: 'Reports',
      path: `/org/${currentOrg.slug}/reports`,
      icon: BarChart3,
    },
  ];

  const adminLinks = [
    {
      name: 'Team & Members',
      path: `/org/${currentOrg.slug}/members`,
      icon: Users,
    },
    {
      name: 'Organization Settings',
      path: `/org/${currentOrg.slug}/settings`,
      icon: Settings,
    },
  ];

  const renderContent = (isMobile: boolean = false) => (
    <aside
      className={`h-full flex flex-col bg-[var(--bg-sidebar)] backdrop-blur-2xl transition-[width] duration-300 ease-in-out select-none text-[var(--text-main)] ${
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
          <nav className="space-y-1">
            {mainLinks.map((link) => {
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
                  <Icon className="w-4 h-4 flex-shrink-0 transition-transform group-hover:scale-110" />
                  {(!isCollapsed || isMobile) && (
                    <span className="flex-1 truncate">{link.name}</span>
                  )}
                  {(!isCollapsed || isMobile) && link.badge && (
                    <span className="text-[10px] px-2 py-0.5 rounded-full bg-[var(--bg-subtle)] text-[var(--text-muted)] font-mono">
                      {link.badge}
                    </span>
                  )}
                </NavLink>
              );
            })}
          </nav>
        </div>

        {/* Administration Section */}
        <div>
          {(!isCollapsed || isMobile) && (
            <div className="px-3 mb-2 text-[10px] font-bold uppercase tracking-wider text-[var(--text-muted)]">
              Administration
            </div>
          )}
          <nav className="space-y-1">
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
                  <Icon className="w-4 h-4 flex-shrink-0 transition-transform group-hover:scale-110" />
                  {(!isCollapsed || isMobile) && <span className="flex-1 truncate">{link.name}</span>}
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
