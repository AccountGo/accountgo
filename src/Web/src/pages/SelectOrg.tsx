import React, { useState } from 'react';
import { useLocation, useNavigate, useSearchParams } from 'react-router-dom';
import {
  Building2,
  Search,
  Plus,
  ArrowRight,
  ShieldCheck,
  CheckCircle2,
  Sparkles,
  LogOut,
  Loader2,
} from 'lucide-react';
import { setPersistedOrgSlug } from '../lib/storage';
import { OrgDetails, Organization, PendingInvitation } from '../types';
import { useAuth } from '../auth/AuthContext';

export const SelectOrg: React.FC = () => {
  const navigate = useNavigate();
  const location = useLocation();
  const [searchParams] = useSearchParams();

  // Legacy deep link: creation now lives on its own page.
  React.useEffect(() => {
    if (searchParams.get('action') === 'new') navigate('/create-org', { replace: true });
  }, [searchParams, navigate]);
  const [searchQuery, setSearchQuery] = useState('');

  // Live database organizations
  const [orgsList, setOrgsList] = useState<OrgDetails[]>([]);
  const [pendingInvitations, setPendingInvitations] = useState<PendingInvitation[]>([]);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [isLoadingInvitations, setIsLoadingInvitations] = useState<boolean>(true);
  const [acceptingInvitation, setAcceptingInvitation] = useState<number | null>(null);
  const [invitationError, setInvitationError] = useState('');

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
        // Only workspaces the caller is an active member of.
        const [response, invitationsResponse] = await Promise.all([
          authFetch('/api/organizations'),
          authFetch('/api/invitations/pending'),
        ]);
        if (response.ok) {
          const data = await response.json() as Organization[];
          if (Array.isArray(data) && mounted) {
            const live: OrgDetails[] = data.map((organization) => ({
              id: organization.id,
              name: organization.name,
              slug: organization.slug,
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
            setOrgsList(live);
            if (invitationsResponse.ok) {
              const pending = await invitationsResponse.json() as PendingInvitation[];
              if (mounted) setPendingInvitations(Array.isArray(pending) ? pending : []);
            } else if (mounted) {
              setInvitationError('Could not load pending invitations.');
            }
          }
        }
      } catch {
        if (mounted) setInvitationError('Could not load your workspaces or invitations.');
      } finally {
        if (mounted) {
          setIsLoading(false);
          setIsLoadingInvitations(false);
        }
      }
    };

    fetchCompanies();
    return () => {
      mounted = false;
    };
  }, [authFetch]);

  React.useEffect(() => {
    if (location.state?.fromSignIn === true &&
        !isLoading && !isLoadingInvitations && !location.state?.notice &&
        !invitationError && orgsList.length === 1 && pendingInvitations.length === 0) {
      handleSelectOrg(orgsList[0]);
    }
  }, [isLoading, isLoadingInvitations, location.state, invitationError, orgsList, pendingInvitations]);

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

  const handleAcceptInvitation = async (invitation: PendingInvitation) => {
    setAcceptingInvitation(invitation.id);
    setInvitationError('');
    try {
      const response = await authFetch(`/api/invitations/pending/${invitation.id}/accept`, { method: 'POST' });
      if (response.status === 403) {
        setInvitationError('Verify your email before accepting this invitation.');
        return;
      }
      if (!response.ok) {
        setInvitationError(response.status === 410
          ? 'This invitation has expired or has already been accepted.'
          : 'Could not accept this invitation. Refresh the page and try again.');
        return;
      }
      const accepted = await response.json() as { slug: string };
      setPendingInvitations((previous) => previous.filter((item) => item.id !== invitation.id));
      setPersistedOrgSlug(accepted.slug);
      navigate(`/org/${encodeURIComponent(accepted.slug)}/dashboard`);
    } catch {
      setInvitationError('Could not accept this invitation. Please try again.');
    } finally {
      setAcceptingInvitation(null);
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
        {typeof location.state?.notice === 'string' && (
          <p role="status" className="max-w-4xl w-full mx-auto mb-4 p-3 rounded-xl bg-amber-500/10 text-amber-300">
            {location.state.notice}
          </p>
        )}
        {invitationError && (
          <p role="alert" className="max-w-4xl w-full mx-auto mb-4 p-3 rounded-xl bg-rose-500/10 text-rose-400">
            {invitationError}
          </p>
        )}

        <section aria-labelledby="pending-invitations-title" className="max-w-4xl w-full mx-auto mb-6 p-4 sm:p-5 rounded-2xl bg-[var(--bg-surface)]">
          <h2 id="pending-invitations-title" className="text-lg font-bold mb-3">Pending invitations</h2>
          {isLoadingInvitations ? (
            <p role="status" className="text-sm text-[var(--text-muted)]">Loading invitations...</p>
          ) : pendingInvitations.length === 0 ? (
            <p className="text-sm text-[var(--text-muted)]">No pending invitations.</p>
          ) : (
            <div className="space-y-3">
              {pendingInvitations.map((invitation) => (
                <div key={invitation.id} className="flex flex-wrap items-center justify-between gap-3 border-t border-[var(--border-subtle)] pt-3">
                  <div>
                    <p className="font-semibold">{invitation.workspaceName}</p>
                    <p className="text-sm text-[var(--text-muted)]">
                      Invited as {invitation.role} · expires {new Date(invitation.expiresAt).toLocaleDateString()}
                    </p>
                  </div>
                  <button
                    type="button"
                    className="btn-primary"
                    disabled={acceptingInvitation !== null}
                    onClick={() => void handleAcceptInvitation(invitation)}
                  >
                    {acceptingInvitation === invitation.id ? 'Accepting...' : 'Accept'}
                  </button>
                </div>
              ))}
            </div>
          )}
        </section>

        {/* Search & Actions Bar (Border-Less) */}
        <div className="flex flex-col sm:flex-row items-stretch sm:items-center gap-3 mb-6">
          <div className="relative flex-1 w-full">
            <Search className="w-4 h-4 text-[var(--text-muted)] absolute left-3.5 top-1/2 -translate-y-1/2 pointer-events-none" />
            <input
              type="text"
              aria-label="Filter organizations by name or slug"
              placeholder="Filter by organization name or slug..."
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              className="w-full h-11 pl-10 pr-4 rounded-xl bg-[var(--bg-subtle)] text-sm text-[var(--text-main)] placeholder:text-[var(--text-muted)] shadow-inner focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)] transition-all"
            />
          </div>

          <button
            type="button"
            onClick={() => navigate('/create-org')}
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
                <button
                  type="button"
                  key={org.id}
                  onClick={() => handleSelectOrg(org)}
                  aria-label={`Enter workspace ${org.name}`}
                  className="group cursor-pointer p-4 sm:p-5 rounded-2xl bg-[var(--bg-surface)] hover:bg-[var(--bg-surface-hover)] backdrop-blur-md shadow-md hover:shadow-2xl transition-all duration-200 flex flex-col justify-between active:scale-[0.99] text-left w-full"
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

                    <h2 className="font-bold text-base text-[var(--text-main)] group-hover:text-[var(--brand-primary)] transition-colors truncate">
                      {org.whitelabel?.brandName ? `${org.name} (${org.whitelabel.brandName})` : org.name}
                    </h2>
                    <p className="text-xs text-[var(--text-muted)] font-mono mt-0.5">
                      /org/{org.slug}
                    </p>
                  </div>

                  <div className="mt-4 pt-3 border-t border-[var(--bg-subtle)] flex items-center justify-between text-xs text-[var(--text-muted)] w-full">
                    <span className="flex items-center gap-1.5">
                      <ShieldCheck className="w-3.5 h-3.5 text-indigo-400" />
                      <span className="px-2 py-0.5 rounded-full bg-[var(--brand-subtle)] text-[var(--brand-primary)] font-semibold">
                        {org.role}
                      </span>
                    </span>
                    <span className="flex items-center gap-1 text-[var(--brand-primary)] font-semibold group-hover:translate-x-1 transition-transform">
                      <span>Enter</span>
                      <ArrowRight className="w-3.5 h-3.5" />
                    </span>
                  </div>
                </button>
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
