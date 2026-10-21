import React, { useState, useEffect, useMemo, useCallback } from 'react';
import { useParams, Outlet, Link, useNavigate } from 'react-router-dom';
import { Building2, AlertCircle, ArrowLeft, RefreshCw } from 'lucide-react';
import { OrgDetails, TenantContextType, WhitelabelConfig, SidebarMode, UserProfile, ModuleKey, PlanKey, SubscriptionInfo } from '../types';
import { setPersistedOrgSlug, clearPersistedOrgSlug } from '../lib/storage';
import { Sidebar } from '../components/Sidebar';
import { Header } from '../components/Header';
import { MobileBottomNav } from '../components/MobileBottomNav';
import { useTheme } from '../themes/ThemeContext';
import { useAuth } from '../auth/AuthContext';

export const OrgLayout: React.FC = () => {
  const { orgSlug } = useParams<{ orgSlug: string }>();
  const navigate = useNavigate();
  const { setTenantWhitelabel } = useTheme();
  const { user: authUser, authFetch } = useAuth();

  const userProfile: UserProfile = useMemo(() => {
    if (!authUser) {
      return {
        id: '',
        name: '',
        email: '',
        role: 'Member',
        permissions: [],
      };
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
      permissions: authUser.permissions || [],
    };
  }, [authUser]);

  const [availableOrgs, setAvailableOrgs] = useState<OrgDetails[]>([]);
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

  // Fetch available organizations directly from live database and resolve tenant
  useEffect(() => {
    let mounted = true;
    setLoading(true);

    const loadAndResolve = async () => {
      try {
        const res = await authFetch('/api/Administration/companies');
        if (res.ok) {
          const data = await res.json();
          if (Array.isArray(data) && mounted) {
            const orgs: OrgDetails[] = data.map((c: any) => ({
              id: c.id?.toString() || c.slug,
              name: c.name,
              slug: c.slug,
              shortName: c.shortName,
              companyCode: c.companyCode,
              cra: c.cra,
              role: 'Admin',
              plan: (c.plan || 'accounting') as PlanKey,
              currency: 'PHP',
              fiscalYearEnd: 'Dec 31',
              avatarBg:
                c.plan === 'invoicing'
                  ? 'bg-gradient-to-br from-violet-500 to-fuchsia-600'
                  : c.plan === 'enterprise'
                  ? 'bg-gradient-to-br from-indigo-500 to-blue-600'
                  : 'bg-gradient-to-br from-emerald-500 to-teal-600',
              whitelabel: {
                themeId: c.plan === 'invoicing' ? 'tokyo-night' : c.plan === 'enterprise' ? 'sapphire' : 'accountgo',
                brandName: c.name,
              },
              subscription: {
                planId: (c.plan || 'accounting') as PlanKey,
                planName: (c.plan || 'accounting').charAt(0).toUpperCase() + (c.plan || 'accounting').slice(1),
                status: c.subscriptionStatus || 'active',
                activeModules: c.activeModules || ['sales'],
                validUntil: '2028-12-31',
              },
            }));
            setAvailableOrgs(orgs);

            const match = orgs.find((o) => o.slug.toLowerCase() === orgSlug?.toLowerCase());
            if (match) {
              try {
                const subRes = await authFetch(`/api/Subscription/current?slug=${match.slug}`);
                if (subRes.ok && mounted) {
                  const subData = await subRes.json();
                  if (subData && subData.plan) {
                    const liveSub: SubscriptionInfo = {
                      planId: subData.plan,
                      planName: subData.plan.charAt(0).toUpperCase() + subData.plan.slice(1),
                      status: subData.subscriptionStatus || 'active',
                      activeModules: subData.activeModules || ['sales'],
                      validUntil: subData.subscriptionValidUntil || '2028-12-31',
                    };
                    match.subscription = liveSub;
                    match.plan = subData.plan;
                  }
                }
              } catch {}

              if (mounted) {
                setCurrentOrg({ ...match });
                setPersistedOrgSlug(match.slug);
                setTenantWhitelabel(match.whitelabel || null);
                setLoading(false);
              }
            } else {
              if (mounted) {
                clearPersistedOrgSlug();
                setCurrentOrg(null);
                setTenantWhitelabel(null);
                setLoading(false);
              }
            }
            return;
          }
        }
        if (mounted) {
          setAvailableOrgs([]);
          setCurrentOrg(null);
          setLoading(false);
        }
      } catch {
        if (mounted) {
          setAvailableOrgs([]);
          setCurrentOrg(null);
          setLoading(false);
        }
      }
    };

    loadAndResolve();
    return () => {
      mounted = false;
    };
  }, [authFetch, orgSlug, setTenantWhitelabel]);

  const subscription: SubscriptionInfo = useMemo(() => {
    if (currentOrg?.subscription) return currentOrg.subscription;
    const plan: PlanKey = currentOrg?.plan || 'accounting';
    const activeModules: ModuleKey[] = plan === 'invoicing'
      ? ['sales']
      : plan === 'enterprise'
      ? ['sales', 'financials', 'purchases', 'inventory', 'reports', 'donations', 'audit']
      : ['sales', 'financials', 'purchases', 'reports'];
    return {
      planId: plan,
      planName: plan.charAt(0).toUpperCase() + plan.slice(1),
      status: 'active',
      activeModules,
      validUntil: '2027-12-31',
    };
  }, [currentOrg]);

  const userPermissions = useMemo<string[]>(() => {
    return userProfile.permissions || [];
  }, [userProfile]);

  const hasModule = useCallback(
    (moduleKey: ModuleKey): boolean => {
      return subscription.activeModules.includes(moduleKey);
    },
    [subscription]
  );

  const hasPermission = useCallback(
    (permission: string | string[]): boolean => {
      if (
        authUser?.roles?.includes('SystemAdministrators') ||
        userProfile.role === 'System Administrator' ||
        currentOrg?.role === 'Owner'
      ) {
        return true;
      }
      const perms = Array.isArray(permission) ? permission : [permission];
      return perms.some((p) => userPermissions.includes(p));
    },
    [authUser, userProfile, currentOrg, userPermissions]
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

  const updateSubscription = async (newPlan: PlanKey, newActiveModules?: ModuleKey[]) => {
    if (!currentOrg) return;
    const resolvedModules: ModuleKey[] = newActiveModules || (
      newPlan === 'invoicing'
        ? ['sales']
        : newPlan === 'enterprise'
        ? ['sales', 'financials', 'purchases', 'inventory', 'reports', 'donations', 'audit']
        : ['sales', 'financials', 'purchases', 'reports']
    );
    const newSub: SubscriptionInfo = {
      planId: newPlan,
      planName: newPlan.charAt(0).toUpperCase() + newPlan.slice(1),
      status: 'active',
      activeModules: resolvedModules,
      validUntil: '2028-12-31',
    };
    const updated = {
      ...currentOrg,
      plan: newPlan,
      subscription: newSub,
    };
    setCurrentOrg(updated);
    setAvailableOrgs((prev) =>
      prev.map((o) => (o.id === updated.id ? updated : o))
    );

    try {
      await authFetch('/api/Subscription/update', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          companyId: Number(currentOrg.id) || undefined,
          slug: currentOrg.slug,
          plan: newPlan,
          activeModules: resolvedModules,
        }),
      });
    } catch {}
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
    updateOrgWhitelabel,
    updateSubscription,
    hasModule,
    hasPermission,
    canAccess,
    sidebarMode,
    setSidebarMode,
  };

  return (
    <div className="flex h-screen w-screen overflow-hidden bg-[var(--bg-canvas)] text-[var(--text-main)] transition-colors duration-200">
      {/* Skip to Main Content Link for Keyboard Users (WCAG 2.4.1) */}
      <a href="#main-content" className="skip-link">
        Skip to main content
      </a>

      {/* Sidebar with active tenant navigation */}
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
          id="main-content"
          tabIndex={-1}
          className={`flex-1 overflow-y-auto p-3 sm:p-4 md:p-6 lg:p-8 pb-20 md:pb-8 bg-[var(--bg-canvas)] smooth-scroll transition-all duration-300 ease-in-out focus:outline-none ${
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

