import React, { useState, useEffect } from 'react';
import { Link, useLocation } from 'react-router-dom';
import {
  Search,
  Bell,
  Coins,
  Calendar,
  Building2,
  ChevronRight,
  Download,
  PanelLeft,
  PanelLeftClose,
} from 'lucide-react';
import { OrgDetails, SidebarMode } from '../types';

interface HeaderProps {
  currentOrg: OrgDetails;
  sidebarMode?: SidebarMode;
  onToggleSidebar?: () => void;
  onMobileToggle?: () => void;
}

export const Header: React.FC<HeaderProps> = ({
  currentOrg,
  sidebarMode = 'expanded',
  onToggleSidebar,
}) => {
  const location = useLocation();
  const [installPrompt, setInstallPrompt] = useState<any>(null);

  // Capture PWA beforeinstallprompt event
  useEffect(() => {
    const handleBeforeInstall = (e: Event) => {
      e.preventDefault();
      setInstallPrompt(e);
    };

    window.addEventListener('beforeinstallprompt', handleBeforeInstall);
    return () => window.removeEventListener('beforeinstallprompt', handleBeforeInstall);
  }, []);

  const handleInstallClick = async () => {
    if (!installPrompt) return;
    installPrompt.prompt();
    const { outcome } = await installPrompt.userChoice;
    if (outcome === 'accepted') {
      setInstallPrompt(null);
    }
  };

  // Extract current section from URL path (e.g., /org/acme-corp/dashboard -> "Dashboard")
  const pathParts = location.pathname.split('/').filter(Boolean);
  const currentSection = pathParts.length > 2 ? pathParts[2] : 'dashboard';
  const sectionTitle =
    currentSection.charAt(0).toUpperCase() + currentSection.slice(1);

  return (
    <header className="no-print h-16 px-3 sm:px-4 md:px-6 bg-[var(--bg-header)] backdrop-blur-xl flex items-center justify-between gap-2 md:gap-4 z-10 flex-shrink-0 safe-top transition-colors duration-200">
      {/* Left: Desktop sidebar toggle + Mobile brand logo + Breadcrumbs */}
      <div className="flex items-center gap-2 sm:gap-3 min-w-0">
        {/* Desktop Sidebar Toggle */}
        <button
          type="button"
          onClick={onToggleSidebar}
          className="hidden md:flex p-2 rounded-xl text-[var(--text-muted)] hover:text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)] active:scale-95 transition-all min-h-[44px] min-w-[44px] items-center justify-center cursor-pointer"
          aria-label={sidebarMode === 'expanded' ? 'Collapse sidebar' : 'Expand sidebar'}
          title={sidebarMode === 'expanded' ? 'Collapse sidebar' : 'Expand sidebar'}
        >
          {sidebarMode === 'expanded' ? (
            <PanelLeftClose className="w-4 h-4" />
          ) : (
            <PanelLeft className="w-4 h-4" />
          )}
        </button>

        <Link
          to={`/org/${currentOrg.slug}/dashboard`}
          className="md:hidden flex items-center shrink-0 group focus:outline-none min-h-[44px] items-center"
          aria-label="AccountGo Dashboard"
        >
          <img
            src="/accountgo-mark.svg"
            alt="AccountGo"
            className="w-7 h-7 rounded-lg shadow-sm group-hover:scale-105 transition-transform"
          />
        </Link>

        {/* Dynamic Breadcrumbs */}
        <nav aria-label="Breadcrumb" className="flex items-center gap-1.5 sm:gap-2 text-xs md:text-sm text-[var(--text-secondary)] min-w-0">
          <Link
            to="/select-org"
            className="hidden sm:flex items-center gap-1 hover:text-[var(--text-main)] transition-colors shrink-0"
            title="Switch Workspace"
          >
            <Building2 className="w-3.5 h-3.5 text-[var(--text-muted)]" />
            <span>Orgs</span>
          </Link>
          <ChevronRight className="hidden sm:inline w-3 h-3 text-[var(--text-muted)] opacity-60 shrink-0" />
          <Link
            to={`/org/${currentOrg.slug}/dashboard`}
            className="font-medium text-[var(--text-main)] hover:opacity-80 transition-opacity truncate max-w-[120px] sm:max-w-[200px]"
          >
            {currentOrg.name}
          </Link>
          <ChevronRight className="w-3 h-3 text-[var(--text-muted)] opacity-60 shrink-0" />
          <span className="text-[var(--brand-primary)] font-semibold truncate capitalize">
            {sectionTitle}
          </span>
        </nav>
      </div>

      {/* Right: Tenant Badges + Search + Notifications */}
      <div className="flex items-center gap-1.5 sm:gap-2.5 shrink-0">
        {/* PWA Install Button */}
        {installPrompt && (
          <button
            type="button"
            onClick={handleInstallClick}
            aria-label="Install AccountGo as an application"
            className="flex items-center gap-1.5 px-3 py-1.5 rounded-xl bg-[var(--brand-subtle)] hover:opacity-90 text-[var(--brand-primary)] text-xs font-semibold transition-all shadow-sm active:scale-95"
            title="Install AccountGo as an application"
          >
            <Download className="w-3.5 h-3.5" />
            <span className="hidden sm:inline">Install App</span>
          </button>
        )}

        {/* Tenant Currency & Fiscal Badges */}
        <div className="hidden lg:flex items-center gap-2">
          {currentOrg.currency && (
            <div className="flex items-center gap-1.5 px-2.5 py-1 rounded-lg bg-[var(--bg-subtle)] text-[11px] text-[var(--text-secondary)] font-mono">
              <Coins className="w-3 h-3 text-[var(--brand-primary)]" />
              <span>{currentOrg.currency}</span>
            </div>
          )}
          {currentOrg.fiscalYearEnd && (
            <div className="flex items-center gap-1.5 px-2.5 py-1 rounded-lg bg-[var(--bg-subtle)] text-[11px] text-[var(--text-secondary)]">
              <Calendar className="w-3 h-3 text-indigo-400" />
              <span>FY: {currentOrg.fiscalYearEnd}</span>
            </div>
          )}
        </div>

        {/* Quick Search trigger */}
        <button
          type="button"
          aria-label="Quick search (Press Command+K)"
          className="hidden md:flex items-center gap-2 px-3 py-1.5 rounded-xl bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)] text-[var(--text-muted)] text-xs cursor-pointer transition-colors shadow-inner"
        >
          <Search className="w-3.5 h-3.5" />
          <span>Search...</span>
          <kbd className="ml-1 font-mono text-[10px] px-1.5 py-0.5 rounded bg-[var(--bg-canvas)] text-[var(--text-secondary)]">
            ⌘K
          </kbd>
        </button>

        {/* Notifications */}
        <button
          type="button"
          className="relative p-2 rounded-xl text-[var(--text-secondary)] hover:text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)] active:scale-95 transition-all min-h-[44px] min-w-[44px] flex items-center justify-center cursor-pointer"
          aria-label="View notifications"
        >
          <Bell className="w-4 h-4" />
          <span className="absolute top-2.5 right-2.5 w-2 h-2 rounded-full bg-[var(--brand-primary)] ring-2 ring-[var(--bg-canvas)]" />
        </button>
      </div>
    </header>
  );
};

export default Header;
