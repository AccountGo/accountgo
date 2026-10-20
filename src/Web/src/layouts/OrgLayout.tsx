import React, { useState, useEffect, useMemo } from 'react';
import { useParams, Outlet, Link, useNavigate } from 'react-router-dom';
import { Building2, AlertCircle, ArrowLeft, RefreshCw } from 'lucide-react';
import { OrgDetails, TenantContextType, WhitelabelConfig, SidebarMode, UserProfile } from '../types';
import { MOCK_ORGS, MOCK_USER, setPersistedOrgSlug } from '../lib/mockData';
import { Sidebar } from '../components/Sidebar';
import { Header } from '../components/Header';
import { MobileBottomNav } from '../components/MobileBottomNav';
import { useTheme } from '../themes/ThemeContext';
import { useAuth } from '../auth/AuthContext';

export const OrgLayout: React.FC = () => {
  const { orgSlug } = useParams<{ orgSlug: string }>();
  const navigate = useNavigate();
  const { setTenantWhitelabel } = useTheme();
  const { user: authUser } = useAuth();

  const userProfile: UserProfile = useMemo(() => {
    if (!authUser) {
      return MOCK_USER;
    }
    const name = [authUser.firstName, authUser.lastName].filter(Boolean).join(' ') || authUser.email.split('@')[0];
    const role = authUser.roles?.includes('SystemAdministrators')
      ? 'System Administrator'
      : (authUser.roles?.[0] || 'Member');
    return {
      id: authUser.id,
      name,
      email: authUser.email,
      role,
    };
  }, [authUser]);

  const [availableOrgs, setAvailableOrgs] = useState<OrgDetails[]>(MOCK_ORGS);
  const [currentOrg, setCurrentOrg] = useState<OrgDetails | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [isMobileOpen, setIsMobileOpen] = useState<boolean>(false);

  const [sidebarMode, setSidebarModeState] = useState<SidebarMode>(() => {
    if (typeof window === 'undefined') return 'expanded';
    try {
      const saved = localStorage.getItem('accountgo:sidebar_mode');
      if (saved === 'expanded' || saved === 'collapsed' || saved === 'closed') {
        return saved;
      }
    } catch {}
    return 'expanded';
  });

  const setSidebarMode = (mode: SidebarMode) => {
    setSidebarModeState(mode);
    try {
      localStorage.setItem('accountgo:sidebar_mode', mode);
    } catch {}
  };

  const toggleSidebarCollapse = () => {
    setSidebarMode(sidebarMode === 'collapsed' ? 'expanded' : 'collapsed');
  };

  useEffect(() => {
    setLoading(true);
    // Simulate tenant resolution/fetch
    const timer = setTimeout(() => {
      const match = availableOrgs.find((o) => o.slug.toLowerCase() === orgSlug?.toLowerCase());
      if (match) {
        setCurrentOrg(match);
        setPersistedOrgSlug(match.slug);
        setTenantWhitelabel(match.whitelabel || null);
      } else {
        setCurrentOrg(null);
        setTenantWhitelabel(null);
      }
      setLoading(false);
    }, 150);

    return () => clearTimeout(timer);
  }, [orgSlug, availableOrgs, setTenantWhitelabel]);

  const switchOrg = (newSlug: string) => {
    navigate(`/org/${newSlug}/dashboard`);
  };

  const updateOrgWhitelabel = (config: WhitelabelConfig) => {
    if (!currentOrg) return;
    const updated = {
      ...currentOrg,
      whitelabel: { ...currentOrg.whitelabel, ...config },
    };
    setCurrentOrg(updated);
    setAvailableOrgs((prev) =>
      prev.map((o) => (o.id === updated.id ? updated : o))
    );
    setTenantWhitelabel(updated.whitelabel);
  };

  // 1. Loading Skeleton State (Border-Less)
  if (loading) {
    return (
      <div className="flex h-screen w-screen items-center justify-center bg-slate-950 text-slate-400 p-4 safe-area-all">
        <div className="flex flex-col items-center gap-4">
          <div className="relative">
            <div className="w-12 h-12 rounded-2xl bg-emerald-500/10 flex items-center justify-center shadow-lg shadow-emerald-500/10 animate-pulse">
              <Building2 className="w-6 h-6 text-emerald-400" />
            </div>
            <RefreshCw className="w-4 h-4 text-emerald-400 animate-spin absolute -bottom-1 -right-1" />
          </div>
          <div className="text-center">
            <h3 className="text-sm font-semibold text-slate-200">Loading workspace</h3>
            <p className="text-xs text-slate-500 font-mono mt-0.5">/org/{orgSlug}</p>
          </div>
        </div>
      </div>
    );
  }

  // 2. Organization Not Found State (Border-Less Surface)
  if (!currentOrg) {
    return (
      <div className="flex h-screen w-screen items-center justify-center bg-slate-950 p-4 sm:p-6 text-slate-200 safe-area-all">
        <div className="max-w-md w-full bg-slate-900/90 backdrop-blur-2xl rounded-3xl p-6 sm:p-8 shadow-2xl shadow-black/80 text-center">
          <div className="w-14 h-14 rounded-2xl bg-rose-500/10 text-rose-400 flex items-center justify-center mx-auto mb-4 shadow-sm">
            <AlertCircle className="w-7 h-7" />
          </div>
          <h2 className="text-xl font-bold text-white mb-1">Organization Not Found</h2>
          <p className="text-sm text-slate-400 mb-6 leading-relaxed">
            The workspace <code className="px-2 py-0.5 rounded-full bg-slate-800 text-rose-300 font-mono text-xs">/org/{orgSlug}</code> does not exist or you do not have permission to access it.
          </p>

          <div className="space-y-3">
            <Link
              to="/select-org"
              className="w-full h-11 inline-flex items-center justify-center gap-2 px-4 rounded-xl bg-emerald-500 hover:bg-emerald-400 text-slate-950 font-bold text-sm transition-all shadow-lg shadow-emerald-500/20 active:scale-[0.98]"
            >
              <ArrowLeft className="w-4 h-4" />
              <span>Browse Available Workspaces</span>
            </Link>
          </div>
        </div>
      </div>
    );
  }

  // 3. Render Scoped Workspace Shell
  const contextValue: TenantContextType = {
    currentOrg,
    availableOrgs,
    user: userProfile,
    switchOrg,
    updateOrgWhitelabel,
    sidebarMode,
    setSidebarMode,
  };

  return (
    <div className="flex h-screen w-screen overflow-hidden bg-[var(--bg-canvas)] text-[var(--text-main)] transition-colors duration-200">
      {/* Sidebar with active tenant navigation */}
      <Sidebar
        currentOrg={currentOrg}
        availableOrgs={availableOrgs}
        user={userProfile}
        sidebarMode={sidebarMode}
        isMobileOpen={isMobileOpen}
        onMobileClose={() => setIsMobileOpen(false)}
      />

      <div className="flex flex-1 flex-col overflow-hidden min-w-0 transition-all duration-300 ease-in-out relative z-10">
        {/* Top Header with Breadcrumbs & Tenant Quick-Actions */}
        <Header
          currentOrg={currentOrg}
          sidebarMode={sidebarMode}
          onToggleSidebar={toggleSidebarCollapse}
          onMobileToggle={() => setIsMobileOpen(!isMobileOpen)}
        />

        {/* Dynamic Page Content with context injection & mobile bottom clearance */}
        <main
          className={`flex-1 overflow-y-auto p-3 sm:p-4 md:p-6 lg:p-8 pb-20 md:pb-8 bg-[var(--bg-canvas)] smooth-scroll transition-all duration-300 ease-in-out ${
            sidebarMode === 'collapsed' ? 'md:pr-20' : sidebarMode === 'closed' ? 'pr-0' : ''
          }`}
        >
          <div className="max-w-7xl mx-auto w-full transition-all duration-300 ease-in-out">
            <Outlet context={contextValue} />
          </div>
        </main>

        {/* Mobile Bottom Navigation Bar */}
        <MobileBottomNav
          currentOrg={currentOrg}
          onOpenMenu={() => setIsMobileOpen(true)}
        />
      </div>
    </div>
  );
};

export default OrgLayout;
