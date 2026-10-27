import React, { useState, useEffect, useMemo, useCallback } from 'react';
import { useParams, Outlet, useNavigate, Link } from 'react-router-dom';
import { Building2, AlertCircle, ArrowLeft, RefreshCw } from 'lucide-react';
import { OrgDetails, TenantContextType, WhitelabelConfig, SidebarMode, UserProfile, ModuleKey, PlanKey, SubscriptionInfo, Organization } from '../types';
import { setPersistedOrgSlug, clearPersistedOrgSlug } from '../lib/storage';
import { Sidebar } from '../components/Sidebar';
import { Header } from '../components/Header';
import { MobileBottomNav } from '../components/MobileBottomNav';
import { useTheme } from '../themes/ThemeContext';
import { useAuth } from '../auth/AuthContext';
import { AssistantMount, AssistantProvider } from '../assistant';
import { PastDueBanner } from '../components/PastDueBanner';

export const OrgLayout: React.FC = () => {
  const { orgSlug } = useParams<{ orgSlug: string }>();
  const navigate = useNavigate();
  const { setTenantWhitelabel } = useTheme();
  const { user: authUser, authFetch } = useAuth();

  const [availableOrgs, setAvailableOrgs] = useState<OrgDetails[]>([]);
  const [currentOrg, setCurrentOrg] = useState<OrgDetails | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [isMobileOpen, setIsMobileOpen] = useState<boolean>(false);
  const userProfile: UserProfile = useMemo(() => ({
    id: authUser?.id || '',
    name: authUser
      ? [authUser.firstName, authUser.lastName].filter(Boolean).join(' ') || authUser.email.split('@')[0]
      : '',
    email: authUser?.email || '',
    role: currentOrg?.role || '',
    permissions: currentOrg?.permissions || [],
  }), [authUser, currentOrg]);

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

  // Resolve both membership and workspace-specific access from the API.
  useEffect(() => {
    let mounted = true;
    setLoading(true);

    const loadAndResolve = async () => {
      try {
        // Only the caller's own active workspaces (tenant-free endpoint).
        const res = await authFetch('/api/organizations');
        if (!res.ok) throw new Error('Could not load workspaces');
        const data = await res.json() as Organization[];
        if (!Array.isArray(data) || !mounted) return;
        const orgs: OrgDetails[] = data.map((organization) => ({
          id: organization.id,
          name: organization.name,
          slug: organization.slug,
          shortName: organization.shortName,
          role: organization.role,
          plan: organization.plan,
          avatarBg: organization.plan === 'invoicing'
            ? 'bg-gradient-to-br from-violet-500 to-fuchsia-600'
            : organization.plan === 'enterprise'
              ? 'bg-gradient-to-br from-indigo-500 to-blue-600'
              : 'bg-gradient-to-br from-emerald-500 to-teal-600',
          whitelabel: {
            themeId: organization.plan === 'invoicing' ? 'tokyo-night' : organization.plan === 'enterprise' ? 'sapphire' : 'accountgo',
            brandName: organization.name,
          },
          subscription: {
            planId: organization.plan,
            planName: organization.plan.charAt(0).toUpperCase() + organization.plan.slice(1),
            status: organization.subscriptionStatus,
            activeModules: organization.activeModules,
          },
        }));
        setAvailableOrgs(orgs);
        const match = orgs.find((organization) => organization.slug.toLowerCase() === orgSlug?.toLowerCase());
        if (!match) {
          if (mounted) {
            clearPersistedOrgSlug();
            setCurrentOrg(null);
            setTenantWhitelabel(null);
            navigate('/select-org', { replace: true, state: { notice: "You don't have access to that workspace" } });
          }
          return;
        }

        const currentResponse = await authFetch('/api/organizations/current');
        if (currentResponse.status === 404) {
          if (mounted) {
            clearPersistedOrgSlug();
            setCurrentOrg(null);
            setTenantWhitelabel(null);
            navigate('/select-org', { replace: true, state: { notice: "You don't have access to that workspace" } });
          }
          return;
        }
        if (!currentResponse.ok) throw new Error('Could not load current workspace');
        const current = await currentResponse.json() as {
          id: number;
          name: string;
          slug: string;
          shortName?: string;
          plan: PlanKey;
          activeModules: ModuleKey[];
          subscriptionStatus: SubscriptionInfo['status'];
          role: OrgDetails['role'];
          permissions: string[];
          currency: string;
        };
        if (mounted) {
          const resolvedOrg: OrgDetails = {
            ...match,
            id: String(current.id),
            name: current.name,
            slug: current.slug,
            shortName: current.shortName,
            plan: current.plan,
            role: current.role,
            currency: current.currency,
            permissions: current.permissions,
            subscription: {
              planId: current.plan,
              planName: current.plan.charAt(0).toUpperCase() + current.plan.slice(1),
              status: current.subscriptionStatus,
              activeModules: current.activeModules,
            },
          };
          setCurrentOrg(resolvedOrg);
          setAvailableOrgs((previous) => previous.map((organization) =>
            organization.slug === resolvedOrg.slug ? resolvedOrg : organization
          ));
          setPersistedOrgSlug(resolvedOrg.slug);
          setTenantWhitelabel(resolvedOrg.whitelabel || null);
        }
        if (mounted) {
          setLoading(false);
        }
      } catch {
        if (mounted) {
          setCurrentOrg(null);
          setLoading(false);
        }
      }
    };

    loadAndResolve();
    return () => {
      mounted = false;
    };
  }, [authFetch, navigate, orgSlug, setTenantWhitelabel]);

  const subscription: SubscriptionInfo = useMemo(() => {
    if (currentOrg?.subscription) return currentOrg.subscription;
    const plan: PlanKey = currentOrg?.plan || 'accounting';
    return {
      planId: plan,
      planName: plan.charAt(0).toUpperCase() + plan.slice(1),
      status: currentOrg?.subscription?.status || 'active',
      activeModules: currentOrg?.subscription?.activeModules || [],
    };
  }, [currentOrg]);

  const userPermissions = useMemo<string[]>(() => {
    return currentOrg?.permissions || [];
  }, [currentOrg]);

  const hasModule = useCallback(
    (moduleKey: ModuleKey): boolean => {
      return subscription.activeModules.includes(moduleKey);
    },
    [subscription]
  );

  const hasPermission = useCallback(
    (permission: string | string[]): boolean => {
      if (currentOrg?.role === 'Owner' || currentOrg?.role === 'Admin') {
        return true;
      }
      const perms = Array.isArray(permission) ? permission : [permission];
      return perms.some((p) => userPermissions.includes(p));
    },
    [currentOrg, userPermissions]
  );

  const canAccess = useCallback(
    (moduleKey: ModuleKey, permission?: string): boolean => {
      if (!hasModule(moduleKey)) return false;
      if (permission && !hasPermission(permission)) return false;
      return true;
    },
    [hasModule, hasPermission]
  );

  const switchOrg = (newSlug: string) => {
    navigate(`/org/${newSlug}/dashboard`);
  };

  const updateCurrentOrg = useCallback((updates: Partial<OrgDetails>) => {
    if (!currentOrg) return;
    const updated = { ...currentOrg, ...updates };
    setCurrentOrg(updated);
    setAvailableOrgs((previous) => previous.map((organization) =>
      organization.id === updated.id ? updated : organization
    ));
  }, [currentOrg]);

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

  const updateSubscription = async (
    newPlan: PlanKey,
    newActiveModules?: ModuleKey[],
    options?: { billingInterval?: 'month' | 'year' },
  ) => {
    if (!currentOrg) return;
    // The server owns the plan → module rules: send the request, then apply only what it returns.
    // Hosted paid upgrades may return { redirectUrl } instead of writing Plan immediately.
    const response = await authFetch('/api/Subscription/update', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        plan: newPlan,
        activeModules: newActiveModules,
        billingInterval: options?.billingInterval,
      }),
    });
    if (!response.ok) {
      let message = `Unable to update subscription (HTTP ${response.status}).`;
      try {
        const body = await response.json();
        if (body?.message) message = body.message;
        else if (body?.detail) message = body.detail;
      } catch { /* keep default */ }
      throw new Error(message);
    }
    const payload = await response.json() as {
      redirectUrl?: string;
      plan?: PlanKey;
      subscriptionStatus?: SubscriptionInfo['status'];
      activeModules?: ModuleKey[];
    };
    if (payload.redirectUrl) {
      window.location.assign(payload.redirectUrl);
      return { redirectUrl: payload.redirectUrl };
    }
    const saved = {
      plan: payload.plan ?? newPlan,
      subscriptionStatus: payload.subscriptionStatus ?? 'active',
      activeModules: payload.activeModules ?? newActiveModules ?? [],
    };
    const newSub: SubscriptionInfo = {
      planId: saved.plan,
      planName: saved.plan.charAt(0).toUpperCase() + saved.plan.slice(1),
      status: saved.subscriptionStatus,
      activeModules: saved.activeModules,
    };
    const updated = {
      ...currentOrg,
      plan: saved.plan,
      subscription: newSub,
    };
    setCurrentOrg(updated);
    setAvailableOrgs((prev) =>
      prev.map((o) => (o.id === updated.id ? updated : o))
    );
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
    subscription,
    userPermissions,
    switchOrg,
    updateCurrentOrg,
    updateOrgWhitelabel,
    updateSubscription,
    hasModule,
    hasPermission,
    canAccess,
    sidebarMode,
    setSidebarMode,
  };

  return (
    <AssistantProvider sidebarMode={sidebarMode} setSidebarMode={setSidebarMode}>
      <div className="app-shell flex h-screen w-screen overflow-hidden bg-[var(--bg-canvas)] text-[var(--text-main)] transition-colors duration-200">
        {/* Skip to Main Content Link for Keyboard Users (WCAG 2.4.1) */}
        <a href="#main-content" className="skip-link">
          Skip to main content
        </a>

        {/* Sidebar with active tenant navigation — outside ChatSidepanelLayout */}
        <Sidebar
          currentOrg={currentOrg}
          availableOrgs={availableOrgs}
          user={userProfile}
          sidebarMode={sidebarMode}
          isMobileOpen={isMobileOpen}
          onMobileClose={() => setIsMobileOpen(false)}
          subscription={subscription}
          hasModule={hasModule}
          hasPermission={hasPermission}
        />

        <AssistantMount
          organizationId={currentOrg.id}
          companySlug={currentOrg.slug}
          companyId={currentOrg.id}
        >
          <div className="app-shell-column flex flex-1 flex-col overflow-hidden min-w-0 h-full transition-all duration-300 ease-in-out relative z-10">
            {/* Top Header with Breadcrumbs & Tenant Quick-Actions */}
            <Header
              currentOrg={currentOrg}
              sidebarMode={sidebarMode}
              onToggleSidebar={toggleSidebarCollapse}
              onMobileToggle={() => setIsMobileOpen(!isMobileOpen)}
            />

            {/* Dynamic Page Content with context injection & mobile bottom clearance */}
            <main
              id="main-content"
              tabIndex={-1}
              className={`flex-1 overflow-y-auto p-3 sm:p-4 md:p-6 lg:p-8 pb-20 md:pb-8 bg-[var(--bg-canvas)] smooth-scroll transition-all duration-300 ease-in-out focus:outline-none ${
                sidebarMode === 'collapsed' ? 'md:pr-20' : sidebarMode === 'closed' ? 'pr-0' : ''
              }`}
            >
              <div className="max-w-7xl mx-auto w-full transition-all duration-300 ease-in-out">
                <PastDueBanner />
                <Outlet context={contextValue} />
              </div>
            </main>

            {/* Mobile Bottom Navigation Bar */}
            <MobileBottomNav
              currentOrg={currentOrg}
              onOpenMenu={() => setIsMobileOpen(true)}
              hasPermission={hasPermission}
            />
          </div>
        </AssistantMount>
      </div>
    </AssistantProvider>
  );
};

export default OrgLayout;
