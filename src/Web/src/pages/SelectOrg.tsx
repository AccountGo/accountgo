import React, { useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import {
  Building2,
  Search,
  Plus,
  ArrowRight,
  ShieldCheck,
  CheckCircle2,
  Sparkles,
  Layers,
  LogOut,
  Loader2,
} from 'lucide-react';
import { setPersistedOrgSlug } from '../lib/storage';
import { OrgDetails } from '../types';
import { useAuth } from '../auth/AuthContext';

export const SelectOrg: React.FC = () => {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const [searchQuery, setSearchQuery] = useState('');
  const [showCreateModal, setShowCreateModal] = useState(
    searchParams.get('action') === 'new'
  );

  // Live database organizations
  const [orgsList, setOrgsList] = useState<OrgDetails[]>([]);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [creating, setCreating] = useState(false);

  // New org form state
  const [newOrgName, setNewOrgName] = useState('');
  const [newOrgSlug, setNewOrgSlug] = useState('');
  const [newOrgCurrency, setNewOrgCurrency] = useState('PHP');

  const { user: authUser, logout, authFetch } = useAuth();
  const userName = authUser
    ? [authUser.firstName, authUser.lastName].filter(Boolean).join(' ') || authUser.email.split('@')[0]
    : 'Admin User';
  const userEmail = authUser?.email || 'admin@accountgo.ph';
  const userInitial = userName.charAt(0).toUpperCase();

  // Load organizations from live database
  React.useEffect(() => {
    let mounted = true;
    const fetchCompanies = async () => {
      try {
        const res = await authFetch('/api/Administration/companies');
        if (res.ok) {
          const data = await res.json();
          if (Array.isArray(data) && mounted) {
            const live: OrgDetails[] = data.map((c: any) => ({
              id: c.id?.toString() || c.slug,
              name: c.name,
              slug: c.slug,
              role: 'Admin',
              plan: c.plan || 'accounting',
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
                planId: c.plan,
                planName: (c.plan || 'accounting').charAt(0).toUpperCase() + (c.plan || 'accounting').slice(1),
                status: c.subscriptionStatus || 'active',
                activeModules: c.activeModules || ['sales'],
                validUntil: '2028-12-31',
              },
            }));
            setOrgsList(live);
          }
        }
      } catch {
      } finally {
        if (mounted) setIsLoading(false);
      }
    };

    fetchCompanies();
    return () => {
      mounted = false;
    };
  }, [authFetch]);

  const handleLogout = async () => {
    await logout();
    navigate('/login');
  };

  const filteredOrgs = orgsList.filter(
    (org) =>
      org.name.toLowerCase().includes(searchQuery.toLowerCase()) ||
      org.slug.toLowerCase().includes(searchQuery.toLowerCase())
  );

  const handleSelectOrg = (org: OrgDetails) => {
    setPersistedOrgSlug(org.slug);
    navigate(`/org/${org.slug}/dashboard`);
  };

  const handleCreateOrg = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newOrgName || !newOrgSlug) return;
    const cleanSlug = newOrgSlug.toLowerCase().replace(/[^a-z0-9-]/g, '-');
    setCreating(true);

    try {
      await authFetch('/api/Administration/company', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          name: newOrgName.trim(),
          slug: cleanSlug,
          plan: 'accounting',
        }),
      });
    } catch {} finally {
      setCreating(false);
      setPersistedOrgSlug(cleanSlug);
      navigate(`/org/${cleanSlug}/dashboard`);
    }
  };

  return (
    <div className="min-h-screen bg-[var(--bg-canvas)] text-[var(--text-main)] flex flex-col justify-between selection:bg-[var(--brand-subtle)] selection:text-[var(--brand-primary)] transition-colors duration-200">
      {/* Top Navigation Bar (Border-Less) */}
      <header className="h-16 px-4 sm:px-6 bg-[var(--bg-header)] backdrop-blur-md flex items-center justify-between safe-top">
        <div className="flex items-center gap-3">
          <img
            src="/accountgo-mark.svg"
            alt="AccountGo"
            className="w-8 h-8 rounded-lg shadow shrink-0"
          />
          <div className="flex items-center gap-2">
            <span className="font-bold text-base tracking-tight text-[var(--text-main)]">
              Account<span className="text-[var(--brand-primary)]">Go</span>
            </span>
            <span className="text-[10px] sm:text-[11px] text-[var(--text-muted)] font-mono px-2 py-0.5 rounded-full bg-[var(--bg-subtle)]">
              Multi-Tenant
            </span>
          </div>
        </div>

        <div className="flex items-center gap-3">
          <div className="text-right hidden sm:block">
            <div className="text-xs font-semibold text-[var(--text-main)]">{userName}</div>
            <div className="text-[11px] text-[var(--text-muted)]">{userEmail}</div>
          </div>
          <div className="w-8 h-8 rounded-xl bg-gradient-to-br from-[var(--brand-primary)] to-emerald-800 text-[var(--brand-text)] flex items-center justify-center font-bold text-xs shadow-sm ring-1 ring-[var(--brand-ring)]">
            {userInitial}
          </div>
          <button
            type="button"
            onClick={handleLogout}
            title="Sign out"
            aria-label="Sign out"
            className="p-2 rounded-xl text-[var(--text-muted)] hover:text-rose-400 hover:bg-rose-500/10 active:scale-95 transition-all cursor-pointer"
          >
            <LogOut className="w-4 h-4" />
          </button>
        </div>
      </header>

      {/* Main Content Area */}
      <main className="flex-1 max-w-4xl w-full mx-auto px-4 py-8 sm:py-12 flex flex-col justify-center">
        <div className="text-center max-w-xl mx-auto mb-8 sm:mb-10">
          <div className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-[var(--brand-subtle)] text-[var(--brand-primary)] text-xs font-semibold mb-3">
            <Sparkles className="w-3.5 h-3.5" />
            <span>Workspace Switcher</span>
          </div>
          <h1 className="text-2xl sm:text-3xl md:text-4xl font-extrabold text-[var(--text-main)] tracking-tight mb-2">
            Select an Organization
          </h1>
          <p className="text-xs sm:text-sm text-[var(--text-muted)] leading-relaxed">
            Choose a workspace to enter or create a new organization. All accounting data, chart of accounts, and permissions are isolated by tenant.
          </p>
        </div>

        {/* Search & Actions Bar (Border-Less) */}
        <div className="flex flex-col sm:flex-row items-stretch sm:items-center gap-3 mb-6">
          <div className="relative flex-1 w-full">
            <Search className="w-4 h-4 text-[var(--text-muted)] absolute left-3.5 top-1/2 -translate-y-1/2" />
            <input
              type="text"
              placeholder="Filter by organization name or slug..."
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              className="w-full h-11 pl-10 pr-4 rounded-xl bg-[var(--bg-subtle)] text-sm text-[var(--text-main)] placeholder:text-[var(--text-muted)] shadow-inner focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)] transition-all"
            />
          </div>

          <button
            type="button"
            onClick={() => setShowCreateModal(true)}
            className="btn-primary !h-11 shrink-0"
          >
            <Plus className="w-4 h-4" />
            <span>New Organization</span>
          </button>
        </div>

        {/* Organizations Grid (Border-Less Cards) */}
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 sm:gap-4">
          {isLoading ? (
            <div className="col-span-full py-16 text-center">
              <Loader2 className="w-8 h-8 text-[var(--brand-primary)] animate-spin mx-auto mb-3" />
              <p className="text-sm text-[var(--text-muted)]">Loading organizations from database...</p>
            </div>
          ) : (
            <>
              {filteredOrgs.map((org) => (
                <div
                  key={org.id}
                  onClick={() => handleSelectOrg(org)}
                  className="group cursor-pointer p-4 sm:p-5 rounded-2xl bg-[var(--bg-surface)] hover:bg-[var(--bg-surface-hover)] backdrop-blur-md shadow-md hover:shadow-2xl transition-all duration-200 flex flex-col justify-between active:scale-[0.99]"
                >
                  <div>
                    <div className="flex items-start justify-between gap-3 mb-3">
                      <div
                        className={`w-11 h-11 rounded-xl flex items-center justify-center font-bold text-base text-white shadow-md ${
                          org.avatarBg || 'bg-gradient-to-br from-indigo-500 to-blue-600'
                        }`}
                      >
                        {org.name.slice(0, 2).toUpperCase()}
                      </div>

                      <div className="flex items-center gap-1.5">
                        {org.plan && (
                          <span className="text-[10px] uppercase font-semibold px-2 py-0.5 rounded-full bg-[var(--bg-subtle)] text-[var(--text-secondary)]">
                            {org.plan}
                          </span>
                        )}
                        {org.currency && (
                          <span className="text-[10px] font-mono font-medium px-2 py-0.5 rounded-full bg-[var(--brand-subtle)] text-[var(--brand-primary)]">
                            {org.currency}
                          </span>
                        )}
                      </div>
                    </div>

                    <h3 className="font-bold text-base text-[var(--text-main)] group-hover:text-[var(--brand-primary)] transition-colors truncate">
                      {org.whitelabel?.brandName ? `${org.name} (${org.whitelabel.brandName})` : org.name}
                    </h3>
                    <p className="text-xs text-[var(--text-muted)] font-mono mt-0.5">
                      /org/{org.slug}
                    </p>
                  </div>

                  <div className="mt-4 pt-3 border-t border-[var(--bg-subtle)] flex items-center justify-between text-xs text-[var(--text-muted)]">
                    <span className="flex items-center gap-1.5">
                      <ShieldCheck className="w-3.5 h-3.5 text-indigo-400" />
                      <span>Role: {org.role || 'Member'}</span>
                    </span>
                    <span className="flex items-center gap-1 text-[var(--brand-primary)] font-semibold group-hover:translate-x-1 transition-transform">
                      <span>Enter</span>
                      <ArrowRight className="w-3.5 h-3.5" />
                    </span>
                  </div>
                </div>
              ))}

              {filteredOrgs.length === 0 && (
                <div className="col-span-full py-12 text-center bg-[var(--bg-surface)] rounded-2xl shadow-inner">
                  <Building2 className="w-8 h-8 text-[var(--text-muted)] mx-auto mb-2" />
                  <p className="text-sm font-medium text-[var(--text-main)]">No organizations found</p>
                  <p className="text-xs text-[var(--text-muted)] mt-1">
                    No organization matches "{searchQuery}". Try a different keyword or create one.
                  </p>
                </div>
              )}
            </>
          )}
        </div>
      </main>

      {/* Create Organization Modal (Border-Less Floating Surface) */}
      {showCreateModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/75 backdrop-blur-md p-4">
          <div className="w-full max-w-md bg-[var(--bg-surface)] backdrop-blur-2xl rounded-2xl p-5 sm:p-6 shadow-2xl shadow-black/80 animate-in fade-in zoom-in-95 duration-150">
            <div className="flex items-center justify-between mb-4">
              <div className="flex items-center gap-2.5">
                <div className="w-9 h-9 rounded-xl bg-[var(--brand-subtle)] text-[var(--brand-primary)] flex items-center justify-center">
                  <Layers className="w-5 h-5" />
                </div>
                <div>
                  <h3 className="font-bold text-[var(--text-main)] text-base">New Organization</h3>
                  <p className="text-xs text-[var(--text-muted)]">Create a dedicated tenant workspace</p>
                </div>
              </div>
              <button
                type="button"
                onClick={() => setShowCreateModal(false)}
                className="w-8 h-8 rounded-lg flex items-center justify-center text-[var(--text-muted)] hover:text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)] text-xs transition-colors cursor-pointer"
                aria-label="Close modal"
              >
                ✕
              </button>
            </div>

            <form onSubmit={handleCreateOrg} className="space-y-4">
              <div>
                <label className="block text-xs font-semibold text-[var(--text-secondary)] mb-1.5">
                  Organization Name
                </label>
                <input
                  type="text"
                  required
                  placeholder="e.g., Global Freight Inc."
                  value={newOrgName}
                  onChange={(e) => {
                    setNewOrgName(e.target.value);
                    if (!newOrgSlug) {
                      setNewOrgSlug(
                        e.target.value.toLowerCase().replace(/[^a-z0-9]/g, '-')
                      );
                    }
                  }}
                  className="w-full h-11 px-3.5 rounded-xl bg-[var(--bg-subtle)] shadow-inner text-sm text-[var(--text-main)] placeholder:text-[var(--text-muted)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)]"
                />
              </div>

              <div>
                <label className="block text-xs font-semibold text-[var(--text-secondary)] mb-1.5">
                  Workspace URL Slug
                </label>
                <div className="flex items-center rounded-xl bg-[var(--bg-subtle)] shadow-inner px-3.5 h-11 text-sm focus-within:ring-2 focus-within:ring-[var(--brand-ring)]">
                  <span className="text-[var(--text-muted)] font-mono text-xs">/org/</span>
                  <input
                    type="text"
                    required
                    placeholder="global-freight"
                    value={newOrgSlug}
                    onChange={(e) => setNewOrgSlug(e.target.value)}
                    className="flex-1 bg-transparent text-[var(--text-main)] font-mono text-xs focus:outline-none ml-1"
                  />
                </div>
                <p className="text-[11px] text-[var(--text-muted)] mt-1">
                  Used in the URL path for tenant isolation and direct deep links.
                </p>
              </div>

              <div>
                <label className="block text-xs font-semibold text-[var(--text-secondary)] mb-1.5">
                  Base Accounting Currency
                </label>
                <select
                  value={newOrgCurrency}
                  onChange={(e) => setNewOrgCurrency(e.target.value)}
                  className="w-full h-11 px-3.5 rounded-xl bg-[var(--bg-subtle)] shadow-inner text-sm text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)] cursor-pointer"
                >
                  <option value="USD">USD — US Dollar ($)</option>
                  <option value="PHP">PHP — Philippine Peso (₱)</option>
                  <option value="EUR">EUR — Euro (€)</option>
                  <option value="GBP">GBP — British Pound (£)</option>
                  <option value="SGD">SGD — Singapore Dollar (S$)</option>
                </select>
              </div>

              <div className="pt-2 flex flex-col-reverse sm:flex-row items-stretch sm:items-center justify-end gap-2.5">
                <button
                  type="button"
                  onClick={() => setShowCreateModal(false)}
                  className="h-11 px-4 rounded-xl text-xs sm:text-sm font-semibold text-[var(--text-secondary)] hover:text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)] transition-colors active:scale-[0.98] cursor-pointer"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={creating}
                  className="btn-primary !h-11"
                >
                  {creating ? 'Creating...' : 'Create & Launch'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Footer */}
      <footer className="py-4 sm:py-6 px-4 sm:px-6 text-center text-xs text-[var(--text-muted)] flex flex-col sm:flex-row items-center justify-between gap-2 safe-bottom">
        <div className="flex items-center gap-2">
          <CheckCircle2 className="w-3.5 h-3.5 text-[var(--brand-primary)]" />
          <span>Tenant Context Root Routing Engine Active</span>
        </div>
        <div className="font-mono text-[11px]">
          AccountGo Cloud API :8001
        </div>
      </footer>
    </div>
  );
};

export default SelectOrg;
