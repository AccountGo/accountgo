import React, { useState } from 'react';
import { useOutletContext, useNavigate } from 'react-router-dom';
import {
  Building2,
  Users,
  Coins,
  ShieldAlert,
  Save,
  Check,
  Calendar,
  Palette,
  Sun,
  Moon,
  Laptop,
} from 'lucide-react';
import { TenantContextType } from '../types';
import { THEMES, SCHEMES, ThemeId, SchemeId } from '../themes';
import { useTheme } from '../themes/ThemeContext';

export const Settings: React.FC = () => {
  const { currentOrg, user, updateOrgWhitelabel } = useOutletContext<TenantContextType>();
  const navigate = useNavigate();
  const { setTheme, setScheme, resolvedScheme } = useTheme();

  const [orgName, setOrgName] = useState(currentOrg.name);
  const [orgSlug, setOrgSlug] = useState(currentOrg.slug);
  const [currency, setCurrency] = useState(currentOrg.currency || 'USD');
  const [fiscalYearEnd, setFiscalYearEnd] = useState(
    currentOrg.fiscalYearEnd || 'Dec 31'
  );

  // Whitelabel state
  const [brandName, setBrandName] = useState(
    currentOrg.whitelabel?.brandName || ''
  );
  const [selectedTheme, setSelectedTheme] = useState<ThemeId>(
    currentOrg.whitelabel?.themeId || 'accountgo'
  );
  const [selectedScheme, setSelectedScheme] = useState<SchemeId>(
    currentOrg.whitelabel?.schemeId || 'dark'
  );
  const [customPrimaryColor, setCustomPrimaryColor] = useState(
    currentOrg.whitelabel?.primaryColor || ''
  );

  const [saved, setSaved] = useState(false);

  const handleSave = (e: React.FormEvent) => {
    e.preventDefault();

    // Update whitelabel config
    if (updateOrgWhitelabel) {
      updateOrgWhitelabel({
        themeId: selectedTheme,
        schemeId: selectedScheme,
        brandName: brandName.trim() || undefined,
        primaryColor: customPrimaryColor.trim() || undefined,
      });
    }

    setSaved(true);
    setTimeout(() => setSaved(false), 2500);

    // If slug changed, navigate to new URL
    if (orgSlug !== currentOrg.slug) {
      navigate(`/org/${orgSlug}/settings`);
    }
  };

  const handlePreviewTheme = (themeId: ThemeId) => {
    setSelectedTheme(themeId);
    setTheme(themeId);
  };

  const handlePreviewScheme = (schemeId: SchemeId) => {
    setSelectedScheme(schemeId);
    setScheme(schemeId);
  };

  return (
    <div className="space-y-6 max-w-4xl animate-in fade-in duration-200 text-[var(--text-main)]">
      {/* Settings Page Header */}
      <div>
        <h1 className="text-xl sm:text-2xl font-bold text-[var(--text-main)] tracking-tight">
          Organization Settings
        </h1>
        <p className="text-xs sm:text-sm text-[var(--text-muted)] mt-1">
          Manage configuration, reporting currency, whitelabeling, and member permissions for{' '}
          <span className="text-[var(--brand-primary)] font-semibold">{currentOrg.name}</span>.
        </p>
      </div>

      <form onSubmit={handleSave} className="space-y-6">
        {/* Card 1: Workspace Profile (Border-Less) */}
        <div className="p-4 sm:p-6 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-4">
          <div className="flex items-center gap-2.5 pb-2">
            <Building2 className="w-5 h-5 text-[var(--brand-primary)]" />
            <div>
              <h2 className="text-sm sm:text-base font-bold text-[var(--text-main)]">General Workspace Profile</h2>
              <p className="text-xs text-[var(--text-muted)]">Basic identification and tenant slug routing</p>
            </div>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <div>
              <label className="block text-xs font-semibold text-[var(--text-secondary)] mb-1.5">
                Organization Legal / Display Name
              </label>
              <input
                type="text"
                required
                value={orgName}
                onChange={(e) => setOrgName(e.target.value)}
                className="w-full h-11 px-3.5 rounded-xl bg-[var(--bg-subtle)] shadow-inner text-sm text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)]"
              />
            </div>

            <div>
              <label className="block text-xs font-semibold text-[var(--text-secondary)] mb-1.5">
                Tenant URL Slug (:orgSlug)
              </label>
              <div className="flex items-center rounded-xl bg-[var(--bg-subtle)] shadow-inner px-3.5 h-11 text-sm focus-within:ring-2 focus-within:ring-[var(--brand-ring)]">
                <span className="text-[var(--text-muted)] font-mono text-xs">/org/</span>
                <input
                  type="text"
                  required
                  value={orgSlug}
                  onChange={(e) => setOrgSlug(e.target.value.toLowerCase().replace(/[^a-z0-9-]/g, '-'))}
                  className="flex-1 bg-transparent text-[var(--brand-primary)] font-mono text-xs focus:outline-none ml-1"
                />
              </div>
              <p className="text-[11px] text-[var(--text-muted)] mt-1">
                Changing this immediately changes deep links and API workspace scoping.
              </p>
            </div>
          </div>
        </div>

        {/* Card 2: Whitelabel & Brand Themes (Border-Less) */}
        <div className="p-4 sm:p-6 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-5">
          <div className="flex items-center gap-2.5 pb-1">
            <Palette className="w-5 h-5 text-[var(--brand-primary)]" />
            <div>
              <h2 className="text-sm sm:text-base font-bold text-[var(--text-main)]">Whitelabel Branding & Theme</h2>
              <p className="text-xs text-[var(--text-muted)]">
                Customize brand name, default themes, and dark/light color schemes for this organization
              </p>
            </div>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <div>
              <label className="block text-xs font-semibold text-[var(--text-secondary)] mb-1.5">
                Whitelabel Brand Header Title
              </label>
              <input
                type="text"
                placeholder="AccountGo (Default)"
                value={brandName}
                onChange={(e) => setBrandName(e.target.value)}
                className="w-full h-11 px-3.5 rounded-xl bg-[var(--bg-subtle)] shadow-inner text-sm text-[var(--text-main)] placeholder:text-[var(--text-muted)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)]"
              />
              <p className="text-[11px] text-[var(--text-muted)] mt-1">
                Custom brand title displayed in the sidebar header and browser tab for this tenant.
              </p>
            </div>

            <div>
              <label className="block text-xs font-semibold text-[var(--text-secondary)] mb-1.5">
                Custom Accent Color Hex (Optional)
              </label>
              <div className="flex items-center gap-2">
                <input
                  type="color"
                  value={customPrimaryColor || '#10b981'}
                  onChange={(e) => setCustomPrimaryColor(e.target.value)}
                  className="w-11 h-11 rounded-xl bg-[var(--bg-subtle)] p-1 cursor-pointer border-none"
                  title="Pick custom brand color"
                />
                <input
                  type="text"
                  placeholder="#10b981"
                  value={customPrimaryColor}
                  onChange={(e) => setCustomPrimaryColor(e.target.value)}
                  className="flex-1 h-11 px-3.5 rounded-xl bg-[var(--bg-subtle)] shadow-inner text-sm font-mono text-[var(--text-main)] placeholder:text-[var(--text-muted)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)]"
                />
              </div>
              <p className="text-[11px] text-[var(--text-muted)] mt-1">
                Overrides the primary brand button and highlight color with custom tenant hex.
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
              Organization Theme Palette
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
        </div>

        {/* Card 3: Accounting & Financial Rules (Border-Less) */}
        <div className="p-4 sm:p-6 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-4">
          <div className="flex items-center gap-2.5 pb-2">
            <Coins className="w-5 h-5 text-indigo-400" />
            <div>
              <h2 className="text-sm sm:text-base font-bold text-[var(--text-main)]">Accounting & Currency Rules</h2>
              <p className="text-xs text-[var(--text-muted)]">Base ledger currency and fiscal reporting boundaries</p>
            </div>
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <div>
              <label className="block text-xs font-semibold text-[var(--text-secondary)] mb-1.5">
                Functional Base Currency
              </label>
              <select
                value={currency}
                onChange={(e) => setCurrency(e.target.value)}
                className="w-full h-11 px-3.5 rounded-xl bg-[var(--bg-subtle)] shadow-inner text-sm text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)] cursor-pointer"
              >
                <option value="USD">USD — United States Dollar ($)</option>
                <option value="PHP">PHP — Philippine Peso (₱)</option>
                <option value="EUR">EUR — Euro (€)</option>
                <option value="GBP">GBP — British Pound (£)</option>
                <option value="SGD">SGD — Singapore Dollar (S$)</option>
              </select>
            </div>

            <div>
              <label className="block text-xs font-semibold text-[var(--text-secondary)] mb-1.5 flex items-center gap-1.5">
                <Calendar className="w-3.5 h-3.5 text-[var(--text-muted)]" />
                <span>Fiscal Year End</span>
              </label>
              <select
                value={fiscalYearEnd}
                onChange={(e) => setFiscalYearEnd(e.target.value)}
                className="w-full h-11 px-3.5 rounded-xl bg-[var(--bg-subtle)] shadow-inner text-sm text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)] cursor-pointer"
              >
                <option value="Dec 31">December 31 (Calendar Year)</option>
                <option value="Jun 30">June 30 (Mid-Year)</option>
                <option value="Mar 31">March 31 (Q1 Fiscal)</option>
                <option value="Sep 30">September 30 (Q3 Fiscal)</option>
              </select>
            </div>
          </div>
        </div>

        {/* Card 4: Team Members (Border-Less) */}
        <div className="p-4 sm:p-6 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-4">
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 pb-2">
            <div className="flex items-center gap-2.5">
              <Users className="w-5 h-5 text-purple-400" />
              <div>
                <h2 className="text-sm sm:text-base font-bold text-[var(--text-main)]">Authorized Tenant Members</h2>
                <p className="text-xs text-[var(--text-muted)]">Users with role-based access to this organization</p>
              </div>
            </div>
            <button
              type="button"
              className="h-10 px-4 self-start sm:self-auto rounded-xl bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)] text-xs font-semibold text-[var(--text-main)] transition-colors shadow-sm active:scale-[0.98] cursor-pointer"
            >
              + Invite Member
            </button>
          </div>

          <div className="space-y-1">
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

            <div className="p-3 rounded-xl bg-[var(--bg-subtle)] flex items-center justify-between gap-3">
              <div className="flex items-center gap-3 min-w-0">
                <div className="w-8 h-8 rounded-xl bg-blue-500/20 text-blue-400 flex items-center justify-center font-bold text-xs ring-1 ring-blue-500/30 shrink-0">
                  A
                </div>
                <div className="min-w-0">
                  <div className="text-xs font-semibold text-[var(--text-main)] truncate">Auditor Account</div>
                  <div className="text-[11px] text-[var(--text-muted)] truncate">auditor@accountgo.ph</div>
                </div>
              </div>
              <span className="px-2.5 py-1 rounded-full bg-[var(--bg-surface-hover)] text-[var(--text-secondary)] text-xs font-semibold shrink-0">
                Viewer / Read-Only
              </span>
            </div>
          </div>
        </div>

        {/* Card 5: Danger Zone (Border-Less) */}
        <div className="p-4 sm:p-6 rounded-2xl bg-rose-950/15 shadow-md space-y-4">
          <div className="flex items-center gap-2.5 text-rose-400">
            <ShieldAlert className="w-5 h-5" />
            <h2 className="text-sm sm:text-base font-bold">Tenant Danger Zone</h2>
          </div>
          <p className="text-xs text-[var(--text-muted)] leading-relaxed">
            Deleting this workspace will purge all journals, invoices, and accounting history for <code className="text-rose-300 font-mono">/org/{currentOrg.slug}</code>. This action is irreversible.
          </p>
          <div className="flex justify-start sm:justify-end">
            <button
              type="button"
              className="h-10 px-4 rounded-xl bg-rose-500/10 hover:bg-rose-500/20 text-rose-300 font-semibold text-xs transition-colors active:scale-[0.98] cursor-pointer"
            >
              Archive or Delete Workspace
            </button>
          </div>
        </div>

        {/* Action Save Bar */}
        <div className="flex flex-col-reverse sm:flex-row items-stretch sm:items-center justify-end gap-3 pt-2">
          {saved && (
            <div className="flex items-center justify-center gap-1.5 text-xs text-[var(--brand-primary)] font-semibold">
              <Check className="w-4 h-4" />
              <span>Settings & Whitelabel Applied</span>
            </div>
          )}
          <button
            type="submit"
            className="btn-primary !h-11 !px-6 text-sm"
          >
            <Save className="w-4 h-4" />
            <span>Save & Apply Whitelabel</span>
          </button>
        </div>
      </form>
    </div>
  );
};

export default Settings;
