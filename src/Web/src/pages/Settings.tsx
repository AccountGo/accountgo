import React, { useState, useEffect, useCallback } from 'react';
import { useOutletContext, useNavigate, useSearchParams, Link } from 'react-router-dom';
import {
  Building2,
  Users,
  Coins,
  Save,
  Check,
  Calendar,
  Palette,
  Sun,
  Moon,
  Laptop,
  Sparkles,
  Shield,
  ArrowRight,
  Clock,
  Landmark,
  CreditCard,
  CheckCircle2,
  AlertCircle,
  ExternalLink,
  RefreshCw,
} from 'lucide-react';
import { TenantContextType, TeamMember } from '../types';
import { THEMES, SCHEMES, ThemeId, SchemeId } from '../themes';
import { useTheme } from '../themes/ThemeContext';
import { useAuth } from '../auth/AuthContext';
import { setPersistedOrgSlug } from '../lib/storage';
import { SubscriptionSettingsTab } from '../components/SubscriptionSettingsTab';
import { RolesSettingsTab } from '../components/RolesSettingsTab';

interface LiveFinancialYear {
  id: number;
  fiscalYearCode: string;
  fiscalYearName: string;
  startDate: string;
  endDate: string;
  isActive: boolean;
}

interface LivePaymentTerm {
  id: number;
  description: string;
  paymentType: string;
  dueAfterDays?: number;
  isActive: boolean;
}

interface LiveCashBank {
  id: number;
  name: string;
  bankName?: string;
  accountNo?: string;
  accountId?: number;
}

export const Settings: React.FC = () => {
  const tenantContext = useOutletContext<TenantContextType>();
  const { currentOrg, user, updateOrgWhitelabel } = tenantContext;
  const { authFetch } = useAuth();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();

  type TabKey = 'general' | 'financials' | 'whitelabel' | 'subscription' | 'roles';
  const currentTab = (searchParams.get('tab') as TabKey) || 'general';

  const handleTabChange = (tab: TabKey) => {
    setSearchParams(tab === 'general' ? {} : { tab });
  };

  const { setTheme, setScheme, resolvedScheme } = useTheme();

  // 1. General Profile State (bound to live database Company table)
  const [companyId, setCompanyId] = useState<number>(Number(currentOrg.id) || 1);
  const [orgName, setOrgName] = useState(currentOrg.name);
  const [shortName, setShortName] = useState(currentOrg.shortName || '');
  const [companyCode, setCompanyCode] = useState(currentOrg.companyCode || '');
  const [cra, setCra] = useState(currentOrg.cra || '');
  const [orgSlug, setOrgSlug] = useState(currentOrg.slug);
  const [currency, setCurrency] = useState(currentOrg.currency || 'PHP');
  const [isLoadingCompany, setIsLoadingCompany] = useState(true);
  const [isSavingCompany, setIsSavingCompany] = useState(false);
  const [companySaveSuccess, setCompanySaveSuccess] = useState(false);
  const [companySaveError, setCompanySaveError] = useState<string | null>(null);

  // 2. Financial & Accounting Rules State
  const [fiscalYears, setFiscalYears] = useState<LiveFinancialYear[]>([]);
  const [paymentTerms, setPaymentTerms] = useState<LivePaymentTerm[]>([]);
  const [cashBanks, setCashBanks] = useState<LiveCashBank[]>([]);
  const [isLoadingFinancials, setIsLoadingFinancials] = useState(false);

  // 3. Authorized Members Summary State
  const [members, setMembers] = useState<TeamMember[]>([]);

  // 4. Whitelabel State
  const [brandName, setBrandName] = useState(currentOrg.whitelabel?.brandName || '');
  const [selectedTheme, setSelectedTheme] = useState<ThemeId>(currentOrg.whitelabel?.themeId || 'accountgo');
  const [selectedScheme, setSelectedScheme] = useState<SchemeId>(currentOrg.whitelabel?.schemeId || 'dark');
  const [customPrimaryColor, setCustomPrimaryColor] = useState(currentOrg.whitelabel?.primaryColor || '');
  const [whitelabelSaved, setWhitelabelSaved] = useState(false);

  // Load live Company details
  const fetchCompanyDetails = useCallback(async () => {
    try {
      setIsLoadingCompany(true);
      const res = await authFetch(`/api/Administration/company?slug=${currentOrg.slug}`);
      if (res.ok) {
        const data = await res.json();
        if (data) {
          setCompanyId(data.id || 1);
          setOrgName(data.name || '');
          setShortName(data.shortName || '');
          setCompanyCode(data.companyCode || '');
          setCra(data.cra || '');
          setOrgSlug(data.slug || currentOrg.slug);
        }
      }
    } catch (err) {
      console.error('Failed to load live company details:', err);
    } finally {
      setIsLoadingCompany(false);
    }
  }, [authFetch, currentOrg.slug]);

  // Load Financials Rules (Fiscal years, payment terms, cash banks)
  const fetchFinancialRules = useCallback(async () => {
    try {
      setIsLoadingFinancials(true);
      const [fyRes, ptRes, cbRes] = await Promise.all([
        authFetch('/api/Administration/financialyears'),
        authFetch('/api/Administration/paymentterms'),
        authFetch('/api/Financials/CashBanks'),
      ]);

      if (fyRes.ok) {
        const fyData = await fyRes.json();
        if (Array.isArray(fyData)) setFiscalYears(fyData);
      }
      if (ptRes.ok) {
        const ptData = await ptRes.json();
        if (Array.isArray(ptData)) setPaymentTerms(ptData);
      }
      if (cbRes.ok) {
        const cbData = await cbRes.json();
        if (Array.isArray(cbData)) setCashBanks(cbData);
      }
    } catch (err) {
      console.error('Failed to load financial rules:', err);
    } finally {
      setIsLoadingFinancials(false);
    }
  }, [authFetch]);

  // Load live team members summary
  const fetchMembers = useCallback(async () => {
    try {
      const res = await authFetch('/api/Administration/users');
      if (res.ok) {
        const data = await res.json();
        if (Array.isArray(data)) setMembers(data);
      }
    } catch {}
  }, [authFetch]);

  useEffect(() => {
    fetchCompanyDetails();
    fetchFinancialRules();
    fetchMembers();
  }, [fetchCompanyDetails, fetchFinancialRules, fetchMembers]);

  // Save General Profile to Database
  const handleSaveCompany = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!orgName.trim() || !orgSlug.trim()) return;

    setIsSavingCompany(true);
    setCompanySaveError(null);
    setCompanySaveSuccess(false);

    const cleanSlug = orgSlug.toLowerCase().replace(/[^a-z0-9-]/g, '-').trim();

    try {
      const res = await authFetch('/api/Administration/company', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          id: companyId,
          name: orgName.trim(),
          shortName: shortName.trim(),
          slug: cleanSlug,
          companyCode: companyCode.trim(),
          cra: cra.trim(),
        }),
      });

      if (!res.ok) {
        const err = await res.json().catch(() => null);
        throw new Error(err?.message || 'Failed to update company in database.');
      }

      await res.json();
      setCompanySaveSuccess(true);
      setTimeout(() => setCompanySaveSuccess(false), 3000);

      // If slug was changed, persist and navigate to the new workspace route
      if (cleanSlug !== currentOrg.slug) {
        setPersistedOrgSlug(cleanSlug);
        navigate(`/org/${cleanSlug}/settings`);
      }
    } catch (err: any) {
      setCompanySaveError(err.message || 'Error updating organization settings.');
    } finally {
      setIsSavingCompany(false);
    }
  };

  // Save Whitelabel Config
  const handleSaveWhitelabel = (e: React.FormEvent) => {
    e.preventDefault();
    if (updateOrgWhitelabel) {
      updateOrgWhitelabel({
        themeId: selectedTheme,
        schemeId: selectedScheme,
        brandName: brandName.trim() || undefined,
        primaryColor: customPrimaryColor.trim() || undefined,
      });
    }

    setWhitelabelSaved(true);
    setTimeout(() => setWhitelabelSaved(false), 2500);
  };

  const handlePreviewTheme = (themeId: ThemeId) => {
    setSelectedTheme(themeId);
    setTheme(themeId);
  };

  const handlePreviewScheme = (schemeId: SchemeId) => {
    setSelectedScheme(schemeId);
    setScheme(schemeId);
  };

  const formatDate = (dateStr: string) => {
    try {
      return new Date(dateStr).toLocaleDateString(undefined, {
        month: 'short',
        day: 'numeric',
        year: 'numeric',
      });
    } catch {
      return dateStr;
    }
  };

  return (
    <div className="space-y-6 w-full animate-in fade-in duration-200 text-[var(--text-main)]">
      {/* Top Workspace Context Banner (Border-Less) */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 p-4 sm:p-5 rounded-2xl bg-gradient-to-r from-[var(--bg-surface)] via-[var(--bg-surface)] to-[var(--brand-subtle)] shadow-xl">
        <div className="space-y-1">
          <div className="flex items-center gap-2">
            <span className="w-2 h-2 rounded-full bg-[var(--brand-primary)] animate-pulse" />
            <span className="text-[10px] sm:text-xs font-semibold uppercase tracking-wider text-[var(--brand-primary)]">
              Workspace Configuration
            </span>
          </div>
          <h1 className="text-xl sm:text-2xl font-bold text-[var(--text-main)] tracking-tight">
            Organization Settings
          </h1>
          <p className="text-xs text-[var(--text-muted)]">
            Manage legal registration, financial rules, whitelabeling, and role-based permissions for{' '}
            <span className="text-[var(--text-main)] font-semibold">{currentOrg.name}</span>.
          </p>
        </div>

        {/* Tenant Meta Pill Badges */}
        <div className="flex flex-wrap items-center gap-2 sm:self-center">
          <div className="px-3 py-1.5 rounded-xl bg-[var(--bg-subtle)] shadow-sm text-xs">
            <span className="text-[var(--text-muted)]">Org ID: </span>
            <code className="text-[var(--text-secondary)] font-mono font-medium">{companyId}</code>
          </div>
          <div className="px-3 py-1.5 rounded-xl bg-[var(--bg-subtle)] shadow-sm text-xs">
            <span className="text-[var(--text-muted)]">Code: </span>
            <code className="text-[var(--text-secondary)] font-mono font-medium">{companyCode || '—'}</code>
          </div>
          <div className="px-3 py-1.5 rounded-xl bg-[var(--bg-subtle)] shadow-sm text-xs">
            <span className="text-[var(--text-muted)]">Slug: </span>
            <code className="text-[var(--brand-primary)] font-mono font-medium">/org/{currentOrg.slug}</code>
          </div>
          <div className="px-3 py-1.5 rounded-xl bg-[var(--brand-subtle)] text-xs text-[var(--brand-primary)] font-medium">
            Plan: {(currentOrg.plan || 'accounting').toUpperCase()}
          </div>
        </div>
      </div>

      {/* Modern Border-Less Tab Controls */}
      <div
        role="tablist"
        aria-label="Organization Settings Sections"
        className="flex items-center gap-1.5 p-1 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-sm w-full sm:w-fit overflow-x-auto"
      >
        <button
          type="button"
          role="tab"
          id="tab-general"
          aria-controls="panel-general"
          aria-selected={currentTab === 'general'}
          onClick={() => handleTabChange('general')}
          className={`flex items-center gap-2 px-4 py-2 rounded-xl text-xs font-semibold transition-all cursor-pointer whitespace-nowrap ${
            currentTab === 'general'
              ? 'bg-[var(--brand-primary)] text-white shadow-sm'
              : 'text-[var(--text-secondary)] hover:text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)]'
          }`}
        >
          <Building2 className="w-3.5 h-3.5" />
          <span>General Profile</span>
        </button>

        <button
          type="button"
          role="tab"
          id="tab-financials"
          aria-controls="panel-financials"
          aria-selected={currentTab === 'financials'}
          onClick={() => handleTabChange('financials')}
          className={`flex items-center gap-2 px-4 py-2 rounded-xl text-xs font-semibold transition-all cursor-pointer whitespace-nowrap ${
            currentTab === 'financials'
              ? 'bg-[var(--brand-primary)] text-white shadow-sm'
              : 'text-[var(--text-secondary)] hover:text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)]'
          }`}
        >
          <Coins className="w-3.5 h-3.5" />
          <span>Financial Rules & Banking</span>
        </button>

        <button
          type="button"
          role="tab"
          id="tab-whitelabel"
          aria-controls="panel-whitelabel"
          aria-selected={currentTab === 'whitelabel'}
          onClick={() => handleTabChange('whitelabel')}
          className={`flex items-center gap-2 px-4 py-2 rounded-xl text-xs font-semibold transition-all cursor-pointer whitespace-nowrap ${
            currentTab === 'whitelabel'
              ? 'bg-[var(--brand-primary)] text-white shadow-sm'
              : 'text-[var(--text-secondary)] hover:text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)]'
          }`}
        >
          <Palette className="w-3.5 h-3.5" />
          <span>Whitelabel & Branding</span>
        </button>

        <button
          type="button"
          role="tab"
          id="tab-subscription"
          aria-controls="panel-subscription"
          aria-selected={currentTab === 'subscription'}
          onClick={() => handleTabChange('subscription')}
          className={`flex items-center gap-2 px-4 py-2 rounded-xl text-xs font-semibold transition-all cursor-pointer whitespace-nowrap ${
            currentTab === 'subscription'
              ? 'bg-[var(--brand-primary)] text-white shadow-sm'
              : 'text-[var(--text-secondary)] hover:text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)]'
          }`}
        >
          <Sparkles className="w-3.5 h-3.5" />
          <span>Subscription & Modules</span>
        </button>

        <button
          type="button"
          role="tab"
          id="tab-roles"
          aria-controls="panel-roles"
          aria-selected={currentTab === 'roles'}
          onClick={() => handleTabChange('roles')}
          className={`flex items-center gap-2 px-4 py-2 rounded-xl text-xs font-semibold transition-all cursor-pointer whitespace-nowrap ${
            currentTab === 'roles'
              ? 'bg-[var(--brand-primary)] text-white shadow-sm'
              : 'text-[var(--text-secondary)] hover:text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)]'
          }`}
        >
          <Shield className="w-3.5 h-3.5" />
          <span>Roles & Permissions</span>
        </button>
      </div>

      {/* ========================================================================= */}
      {/* TAB 1: GENERAL PROFILE (Live Database Company Table)                      */}
      {/* ========================================================================= */}
      {currentTab === 'general' && (
        <div
          role="tabpanel"
          id="panel-general"
          aria-labelledby="tab-general"
          tabIndex={0}
          className="space-y-6 focus:outline-none"
        >
          <form onSubmit={handleSaveCompany} className="space-y-6">
            {/* Feedback Notifications */}
            {companySaveSuccess && (
              <div
                role="alert"
                aria-live="polite"
                className="p-4 rounded-2xl bg-emerald-500/10 text-emerald-400 text-xs font-semibold flex items-center gap-2.5 shadow-sm"
              >
                <CheckCircle2 className="w-4 h-4 shrink-0" />
                <span>Organization profile updated successfully in live SQLite database.</span>
              </div>
            )}
            {companySaveError && (
              <div
                role="alert"
                aria-live="assertive"
                className="p-4 rounded-2xl bg-rose-500/10 text-rose-400 text-xs font-semibold flex items-center gap-2.5 shadow-sm"
              >
                <AlertCircle className="w-4 h-4 shrink-0" />
                <span>{companySaveError}</span>
              </div>
            )}

            {/* Profile Card */}
            <div className="p-4 sm:p-6 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-4">
              <div className="flex items-center justify-between pb-2 border-b border-[var(--border-subtle)]">
                <div className="flex items-center gap-2.5">
                  <Building2 className="w-5 h-5 text-[var(--brand-primary)]" />
                  <div>
                    <h2 className="text-sm sm:text-base font-bold text-[var(--text-main)]">
                      Legal Entity & Registration
                    </h2>
                    <p className="text-xs text-[var(--text-muted)]">
                      Persistent attributes mapped to the active Company database entity
                    </p>
                  </div>
                </div>
                {isLoadingCompany && (
                  <RefreshCw className="w-4 h-4 text-[var(--brand-primary)] animate-spin" />
                )}
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                <div>
                  <label htmlFor="org-name-input" className="block text-xs font-semibold text-[var(--text-secondary)] mb-1.5">
                    Organization Legal Name
                  </label>
                  <input
                    id="org-name-input"
                    type="text"
                    required
                    value={orgName}
                    onChange={(e) => setOrgName(e.target.value)}
                    placeholder="e.g. Financial Solutions Inc."
                    className="w-full h-11 px-3.5 rounded-xl bg-[var(--bg-subtle)] shadow-inner text-sm text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)]"
                  />
                  <p className="text-[11px] text-[var(--text-muted)] mt-1">
                    Official business name printed on sales invoices and tax receipts.
                  </p>
                </div>

                <div>
                  <label htmlFor="short-name-input" className="block text-xs font-semibold text-[var(--text-secondary)] mb-1.5">
                    Short Name / Trading Acronym
                  </label>
                  <input
                    id="short-name-input"
                    type="text"
                    value={shortName}
                    onChange={(e) => setShortName(e.target.value.toUpperCase())}
                    placeholder="e.g. FSI"
                    className="w-full h-11 px-3.5 rounded-xl bg-[var(--bg-subtle)] shadow-inner text-sm font-mono text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)]"
                  />
                  <p className="text-[11px] text-[var(--text-muted)] mt-1">
                    Compact brand badge used across header pills and tenant avatars.
                  </p>
                </div>

                <div>
                  <label htmlFor="company-code-input" className="block text-xs font-semibold text-[var(--text-secondary)] mb-1.5">
                    Internal Company Code
                  </label>
                  <input
                    id="company-code-input"
                    type="text"
                    value={companyCode}
                    onChange={(e) => setCompanyCode(e.target.value)}
                    placeholder="e.g. 100"
                    className="w-full h-11 px-3.5 rounded-xl bg-[var(--bg-subtle)] shadow-inner text-sm font-mono text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)]"
                  />
                  <p className="text-[11px] text-[var(--text-muted)] mt-1">
                    Unique identifier used in ERP general ledger journal sequences.
                  </p>
                </div>

                <div>
                  <label htmlFor="cra-input" className="block text-xs font-semibold text-[var(--text-secondary)] mb-1.5">
                    CRA / Tax Registration Number
                  </label>
                  <input
                    id="cra-input"
                    type="text"
                    value={cra}
                    onChange={(e) => setCra(e.target.value)}
                    placeholder="e.g. 012345678"
                    className="w-full h-11 px-3.5 rounded-xl bg-[var(--bg-subtle)] shadow-inner text-sm font-mono text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)]"
                  />
                  <p className="text-[11px] text-[var(--text-muted)] mt-1">
                    Tax authority identification number for GST/HST/VAT reporting.
                  </p>
                </div>

                <div>
                  <label htmlFor="currency-input" className="block text-xs font-semibold text-[var(--text-secondary)] mb-1.5">
                    Functional Base Currency
                  </label>
                  <select
                    id="currency-input"
                    value={currency}
                    onChange={(e) => setCurrency(e.target.value)}
                    className="w-full h-11 px-3.5 rounded-xl bg-[var(--bg-subtle)] shadow-inner text-sm font-mono text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)] cursor-pointer"
                  >
                    <option value="PHP">PHP - Philippine Peso (₱)</option>
                    <option value="USD">USD - United States Dollar ($)</option>
                    <option value="CAD">CAD - Canadian Dollar ($)</option>
                    <option value="EUR">EUR - Euro (€)</option>
                    <option value="GBP">GBP - British Pound (£)</option>
                  </select>
                  <p className="text-[11px] text-[var(--text-muted)] mt-1">
                    Reporting currency configured for general ledger presentation.
                  </p>
                </div>

                <div className="sm:col-span-1">
                  <label htmlFor="org-slug-input" className="block text-xs font-semibold text-[var(--text-secondary)] mb-1.5">
                    Tenant Workspace URL Slug (:orgSlug)
                  </label>
                  <div className="flex items-center rounded-xl bg-[var(--bg-subtle)] shadow-inner px-3.5 h-11 text-sm focus-within:ring-2 focus-within:ring-[var(--brand-ring)]">
                    <span className="text-[var(--text-muted)] font-mono text-xs">/org/</span>
                    <input
                      id="org-slug-input"
                      type="text"
                      required
                      value={orgSlug}
                      onChange={(e) => setOrgSlug(e.target.value.toLowerCase().replace(/[^a-z0-9-]/g, '-'))}
                      className="flex-1 bg-transparent text-[var(--brand-primary)] font-mono text-xs font-bold focus:outline-none ml-1"
                    />
                  </div>
                  <p className="text-[11px] text-[var(--text-muted)] mt-1">
                    Changing the workspace slug immediately updates browser routing and deep link scoping.
                  </p>
                </div>
              </div>

              {/* Save Button */}
              <div className="pt-3 flex items-center justify-end">
                <button
                  type="submit"
                  disabled={isSavingCompany}
                  className="btn-primary !h-11 !px-6 text-sm flex items-center gap-2"
                >
                  {isSavingCompany ? (
                    <>
                      <RefreshCw className="w-4 h-4 animate-spin" />
                      <span>Saving Database...</span>
                    </>
                  ) : (
                    <>
                      <Save className="w-4 h-4" />
                      <span>Save Entity Profile</span>
                    </>
                  )}
                </button>
              </div>
            </div>
          </form>

          {/* Authorized Members Card (Border-Less) */}
          <div className="p-4 sm:p-6 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-4">
            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 pb-2 border-b border-[var(--border-subtle)]">
              <div className="flex items-center gap-2.5">
                <Users className="w-5 h-5 text-purple-400" />
                <div>
                  <h2 className="text-sm sm:text-base font-bold text-[var(--text-main)]">
                    Authorized Tenant Members
                  </h2>
                  <p className="text-xs text-[var(--text-muted)]">
                    Users with live role-based access to this organization in the database
                  </p>
                </div>
              </div>
              <Link
                to={`/org/${currentOrg.slug}/members`}
                className="h-10 px-4 self-start sm:self-auto rounded-xl bg-[var(--brand-primary)] hover:brightness-110 text-white text-xs font-semibold transition-all shadow-sm active:scale-[0.98] flex items-center gap-1.5 cursor-pointer"
              >
                <span>Manage All Members</span>
                <ArrowRight className="w-3.5 h-3.5" />
              </Link>
            </div>

            <div className="space-y-2">
              {members.length === 0 ? (
                <div className="p-3 rounded-xl bg-[var(--bg-subtle)] flex items-center justify-between gap-3">
                  <div className="flex items-center gap-3 min-w-0">
                    <div className="w-8 h-8 rounded-xl bg-[var(--brand-subtle)] text-[var(--brand-primary)] flex items-center justify-center font-bold text-xs ring-1 ring-[var(--brand-ring)] shrink-0">
                      {user.name.charAt(0)}
                    </div>
                    <div className="min-w-0">
                      <div className="text-xs font-semibold text-[var(--text-main)] flex items-center gap-2 truncate">
                        <span>{user.name}</span>
                        <span className="text-[10px] text-[var(--text-muted)]">(You)</span>
                      </div>
                      <div className="text-[11px] text-[var(--text-muted)] truncate">{user.email}</div>
                    </div>
                  </div>
                  <span className="px-2.5 py-1 rounded-full bg-[var(--brand-subtle)] text-[var(--brand-primary)] text-xs font-semibold shrink-0">
                    Owner / Admin
                  </span>
                </div>
              ) : (
                members.slice(0, 4).map((m) => {
                  const fullName = `${m.firstName || ''} ${m.lastName || ''}`.trim() || m.userName || m.email;
                  const isCurrentUser = user.email?.toLowerCase() === m.email?.toLowerCase();
                  const isSysAdmin = m.sysAdmin || (m.roles || []).some((r) => r.sysAdmin || r.name === 'SystemAdministrators');

                  return (
                    <div key={m.id} className="p-3 rounded-xl bg-[var(--bg-subtle)] flex items-center justify-between gap-3">
                      <div className="flex items-center gap-3 min-w-0">
                        <div
                          className={`w-8 h-8 rounded-xl flex items-center justify-center font-bold text-xs ring-1 shrink-0 ${
                            isSysAdmin
                              ? 'bg-purple-500/15 text-purple-400 ring-purple-500/30'
                              : 'bg-[var(--brand-subtle)] text-[var(--brand-primary)] ring-[var(--brand-ring)]'
                          }`}
                        >
                          {fullName.charAt(0).toUpperCase()}
                        </div>
                        <div className="min-w-0">
                          <div className="text-xs font-semibold text-[var(--text-main)] flex items-center gap-2 truncate">
                            <span>{fullName}</span>
                            {isCurrentUser && (
                              <span className="text-[10px] text-[var(--text-muted)]">(You)</span>
                            )}
                          </div>
                          <div className="text-[11px] text-[var(--text-muted)] truncate">{m.email}</div>
                        </div>
                      </div>
                      <div className="flex items-center gap-1.5 shrink-0">
                        {m.roles && m.roles.length > 0 ? (
                          m.roles.map((r) => (
                            <span
                              key={r.id}
                              className={`px-2.5 py-1 rounded-full text-xs font-semibold ${
                                r.sysAdmin || r.name === 'SystemAdministrators'
                                  ? 'bg-purple-500/15 text-purple-400'
                                  : 'bg-[var(--bg-surface-hover)] text-[var(--text-secondary)]'
                              }`}
                            >
                              {r.displayName || r.name}
                            </span>
                          ))
                        ) : (
                          <span className="px-2.5 py-1 rounded-full bg-[var(--bg-surface-hover)] text-[var(--text-muted)] text-xs font-semibold">
                            Member
                          </span>
                        )}
                      </div>
                    </div>
                  );
                })
              )}
            </div>
          </div>
        </div>
      )}

      {/* ========================================================================= */}
      {/* TAB 2: FINANCIAL RULES & BANKING (Live SQLite Database Tables)            */}
      {/* ========================================================================= */}
      {currentTab === 'financials' && (
        <div
          role="tabpanel"
          id="panel-financials"
          aria-labelledby="tab-financials"
          tabIndex={0}
          className="space-y-6 focus:outline-none"
        >
          {/* Fiscal Year Section */}
          <div className="p-4 sm:p-6 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-4">
            <div className="flex items-center justify-between pb-2 border-b border-[var(--border-subtle)]">
              <div className="flex items-center gap-2.5">
                <Calendar className="w-5 h-5 text-indigo-400" />
                <div>
                  <h2 className="text-sm sm:text-base font-bold text-[var(--text-main)]">
                    Active Fiscal Reporting Year
                  </h2>
                  <p className="text-xs text-[var(--text-muted)]">
                    Financial boundaries defined in the database FinancialYear table
                  </p>
                </div>
              </div>
              {isLoadingFinancials && (
                <RefreshCw className="w-4 h-4 text-indigo-400 animate-spin" />
              )}
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
              {fiscalYears.length > 0 ? (
                fiscalYears.map((fy) => (
                  <div
                    key={fy.id}
                    className="p-4 rounded-xl bg-[var(--bg-subtle)] space-y-2 border border-[var(--border-subtle)]"
                  >
                    <div className="flex items-center justify-between">
                      <span className="text-xs font-bold text-[var(--text-main)]">
                        {fy.fiscalYearName}
                      </span>
                      <span
                        className={`px-2 py-0.5 rounded-full text-[10px] font-bold ${
                          fy.isActive
                            ? 'bg-emerald-500/15 text-emerald-400'
                            : 'bg-slate-700 text-slate-300'
                        }`}
                      >
                        {fy.isActive ? 'Active Period' : 'Closed'}
                      </span>
                    </div>
                    <div className="text-xs text-[var(--text-muted)] font-mono">
                      Code: <strong className="text-[var(--text-secondary)]">{fy.fiscalYearCode}</strong>
                    </div>
                    <div className="text-[11px] text-[var(--text-muted)] flex items-center justify-between pt-1 border-t border-[var(--border-subtle)]">
                      <span>{formatDate(fy.startDate)}</span>
                      <span>&rarr;</span>
                      <span>{formatDate(fy.endDate)}</span>
                    </div>
                  </div>
                ))
              ) : (
                <div className="col-span-full p-4 rounded-xl bg-[var(--bg-subtle)] text-xs text-[var(--text-muted)] text-center">
                  No fiscal year records found in database.
                </div>
              )}
            </div>
          </div>

          {/* Operating Cash & Bank Accounts */}
          <div className="p-4 sm:p-6 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-4">
            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 pb-2 border-b border-[var(--border-subtle)]">
              <div className="flex items-center gap-2.5">
                <Landmark className="w-5 h-5 text-emerald-400" />
                <div>
                  <h2 className="text-sm sm:text-base font-bold text-[var(--text-main)]">
                    Operating Cash & Bank Accounts
                  </h2>
                  <p className="text-xs text-[var(--text-muted)]">
                    Settlement accounts from the database Bank table linked to the General Ledger
                  </p>
                </div>
              </div>
              <Link
                to={`/org/${currentOrg.slug}/financials`}
                className="h-9 px-3 self-start sm:self-auto rounded-xl bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)] text-xs font-semibold text-[var(--text-main)] transition-colors flex items-center gap-1.5 cursor-pointer"
              >
                <span>View Full Chart of Accounts</span>
                <ExternalLink className="w-3.5 h-3.5" />
              </Link>
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              {cashBanks.length > 0 ? (
                cashBanks.map((bank) => (
                  <div
                    key={bank.id}
                    className="p-4 rounded-xl bg-[var(--bg-subtle)] space-y-2 border border-[var(--border-subtle)]"
                  >
                    <div className="flex items-center justify-between">
                      <div className="flex items-center gap-2">
                        <CreditCard className="w-4 h-4 text-emerald-400" />
                        <span className="text-xs font-bold text-[var(--text-main)]">
                          {bank.name}
                        </span>
                      </div>
                      <span className="text-[10px] font-mono px-2 py-0.5 rounded bg-[var(--bg-canvas)] text-[var(--brand-primary)]">
                        Account #{bank.accountId}
                      </span>
                    </div>
                    <div className="grid grid-cols-2 gap-2 text-xs text-[var(--text-muted)] pt-1">
                      <div>
                        <span>Bank: </span>
                        <strong className="text-[var(--text-secondary)]">{bank.bankName || 'Direct Cash'}</strong>
                      </div>
                      <div>
                        <span>Number: </span>
                        <strong className="text-[var(--text-secondary)] font-mono">{bank.accountNo || '—'}</strong>
                      </div>
                    </div>
                  </div>
                ))
              ) : (
                <div className="col-span-full p-4 rounded-xl bg-[var(--bg-subtle)] text-xs text-[var(--text-muted)] text-center">
                  No bank accounts configured in database.
                </div>
              )}
            </div>
          </div>

          {/* Payment Terms */}
          <div className="p-4 sm:p-6 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-4">
            <div className="flex items-center gap-2.5 pb-2 border-b border-[var(--border-subtle)]">
              <Clock className="w-5 h-5 text-amber-400" />
              <div>
                <h2 className="text-sm sm:text-base font-bold text-[var(--text-main)]">
                  Standard Customer & Vendor Payment Terms
                </h2>
                <p className="text-xs text-[var(--text-muted)]">
                  Payment scheduling rules loaded from the PaymentTerm database table
                </p>
              </div>
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
              {paymentTerms.map((pt) => (
                <div
                  key={pt.id}
                  className="p-3.5 rounded-xl bg-[var(--bg-subtle)] flex items-center justify-between border border-[var(--border-subtle)]"
                >
                  <div>
                    <div className="text-xs font-semibold text-[var(--text-main)]">
                      {pt.description}
                    </div>
                    <div className="text-[10px] text-[var(--text-muted)] font-mono mt-0.5">
                      Net {pt.dueAfterDays || 0} days
                    </div>
                  </div>
                  <span className="px-2 py-0.5 rounded text-[10px] font-bold bg-amber-500/10 text-amber-400">
                    Active
                  </span>
                </div>
              ))}
            </div>
          </div>
        </div>
      )}

      {/* ========================================================================= */}
      {/* TAB 3: WHITELABEL BRANDING & THEME                                        */}
      {/* ========================================================================= */}
      {currentTab === 'whitelabel' && (
        <form
          role="tabpanel"
          id="panel-whitelabel"
          aria-labelledby="tab-whitelabel"
          tabIndex={0}
          onSubmit={handleSaveWhitelabel}
          className="space-y-6 focus:outline-none"
        >
          {whitelabelSaved && (
            <div
              role="alert"
              aria-live="polite"
              className="p-4 rounded-2xl bg-emerald-500/10 text-emerald-400 text-xs font-semibold flex items-center gap-2.5 shadow-sm"
            >
              <CheckCircle2 className="w-4 h-4 shrink-0" />
              <span>Whitelabel branding and palette settings applied successfully.</span>
            </div>
          )}

          <div className="p-4 sm:p-6 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-5">
            <div className="flex items-center gap-2.5 pb-1 border-b border-[var(--border-subtle)]">
              <Palette className="w-5 h-5 text-[var(--brand-primary)]" />
              <div>
                <h2 className="text-sm sm:text-base font-bold text-[var(--text-main)]">
                  Whitelabel Branding & Custom Theme
                </h2>
                <p className="text-xs text-[var(--text-muted)]">
                  Personalize the tenant interface with custom titles, colors, and design themes
                </p>
              </div>
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label htmlFor="brand-name-input" className="block text-xs font-semibold text-[var(--text-secondary)] mb-1.5">
                  Whitelabel Header Title
                </label>
                <input
                  id="brand-name-input"
                  type="text"
                  placeholder="AccountGo (Default)"
                  value={brandName}
                  onChange={(e) => setBrandName(e.target.value)}
                  className="w-full h-11 px-3.5 rounded-xl bg-[var(--bg-subtle)] shadow-inner text-sm text-[var(--text-main)] placeholder:text-[var(--text-muted)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)]"
                />
                <p className="text-[11px] text-[var(--text-muted)] mt-1">
                  Custom brand title shown in the top header and browser tab for this workspace.
                </p>
              </div>

              <div>
                <label htmlFor="custom-accent-color-hex" className="block text-xs font-semibold text-[var(--text-secondary)] mb-1.5">
                  Custom Accent Color Hex (Optional)
                </label>
                <div className="flex items-center gap-2">
                  <input
                    id="custom-accent-color-picker"
                    type="color"
                    aria-label="Pick custom brand color"
                    value={customPrimaryColor || '#10b981'}
                    onChange={(e) => setCustomPrimaryColor(e.target.value)}
                    className="w-11 h-11 rounded-xl bg-[var(--bg-subtle)] p-1 cursor-pointer border-none"
                    title="Pick custom brand color"
                  />
                  <input
                    id="custom-accent-color-hex"
                    type="text"
                    placeholder="#10b981"
                    value={customPrimaryColor}
                    onChange={(e) => setCustomPrimaryColor(e.target.value)}
                    className="flex-1 h-11 px-3.5 rounded-xl bg-[var(--bg-subtle)] shadow-inner text-sm font-mono text-[var(--text-main)] placeholder:text-[var(--text-muted)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)]"
                  />
                </div>
                <p className="text-[11px] text-[var(--text-muted)] mt-1">
                  Overrides primary button highlights and badge glows across the UI.
                </p>
              </div>
            </div>

            {/* Scheme Selector */}
            <div>
              <label className="block text-xs font-semibold text-[var(--text-secondary)] mb-2">
                Default Color Scheme
              </label>
              <div className="grid grid-cols-3 gap-2 sm:gap-3">
                {SCHEMES.map((s) => {
                  const isSelected = selectedScheme === s.id;
                  const Icon = s.id === 'dark' ? Moon : s.id === 'light' ? Sun : Laptop;
                  return (
                    <button
                      key={s.id}
                      type="button"
                      onClick={() => handlePreviewScheme(s.id as SchemeId)}
                      className={`flex items-center justify-center gap-2 h-11 rounded-xl text-xs font-semibold transition-all cursor-pointer ${
                        isSelected
                          ? 'bg-[var(--brand-primary)] text-[var(--brand-text)] shadow-md'
                          : 'bg-[var(--bg-subtle)] text-[var(--text-secondary)] hover:bg-[var(--bg-surface-hover)]'
                      }`}
                    >
                      <Icon className="w-4 h-4" />
                      <span>{s.label}</span>
                    </button>
                  );
                })}
              </div>
            </div>

            {/* Theme Palette Cards */}
            <div>
              <label className="block text-xs font-semibold text-[var(--text-secondary)] mb-2">
                Curated Theme Palettes
              </label>
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
                {THEMES.map((t) => {
                  const isSelected = selectedTheme === t.id;
                  const palette = t.colors[resolvedScheme];
                  return (
                    <button
                      key={t.id}
                      type="button"
                      onClick={() => handlePreviewTheme(t.id as ThemeId)}
                      className={`p-3.5 rounded-2xl text-left transition-all cursor-pointer ${
                        isSelected
                          ? 'bg-[var(--brand-subtle)] ring-2 ring-[var(--brand-primary)]'
                          : 'bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)]'
                      }`}
                    >
                      <div className="flex items-center justify-between mb-2">
                        <div className="flex items-center gap-1.5 -space-x-1">
                          <div
                            className="w-4 h-4 rounded-full ring-2 ring-[var(--bg-surface)] shadow-sm"
                            style={{ backgroundColor: palette.brandPrimary }}
                          />
                          <div
                            className="w-4 h-4 rounded-full ring-2 ring-[var(--bg-surface)] shadow-sm"
                            style={{ backgroundColor: palette.bgSurface }}
                          />
                        </div>
                        {isSelected && (
                          <Check className="w-4 h-4 text-[var(--brand-primary)] stroke-[3]" />
                        )}
                      </div>
                      <div className="text-xs font-bold text-[var(--text-main)] truncate">
                        {t.label}
                      </div>
                      <div className="text-[11px] text-[var(--text-muted)] truncate mt-0.5">
                        {t.description}
                      </div>
                    </button>
                  );
                })}
              </div>
            </div>

            {/* Save Whitelabel Action */}
            <div className="pt-2 flex justify-end">
              <button
                type="submit"
                className="btn-primary !h-11 !px-6 text-sm flex items-center gap-2"
              >
                <Save className="w-4 h-4" />
                <span>Apply Whitelabel Settings</span>
              </button>
            </div>
          </div>
        </form>
      )}

      {/* ========================================================================= */}
      {/* TAB 4: SUBSCRIPTION & CENTRAL MODULES (Live Database Company Plan)        */}
      {/* ========================================================================= */}
      {currentTab === 'subscription' && (
        <div
          role="tabpanel"
          id="panel-subscription"
          aria-labelledby="tab-subscription"
          tabIndex={0}
          className="focus:outline-none"
        >
          <SubscriptionSettingsTab tenantContext={tenantContext} />
        </div>
      )}

      {/* ========================================================================= */}
      {/* TAB 5: ROLES & PERMISSIONS (Live Database SecurityRole Table)             */}
      {/* ========================================================================= */}
      {currentTab === 'roles' && (
        <div
          role="tabpanel"
          id="panel-roles"
          aria-labelledby="tab-roles"
          tabIndex={0}
          className="focus:outline-none"
        >
          <RolesSettingsTab tenantContext={tenantContext} />
        </div>
      )}
    </div>
  );
};

export default Settings;
