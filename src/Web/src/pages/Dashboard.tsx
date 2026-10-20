import React from 'react';
import { useOutletContext, Link } from 'react-router-dom';
import {
  TrendingUp,
  Receipt,
  Wallet,
  BookOpen,
  ArrowUpRight,
  Plus,
  ShieldCheck,
  CheckCircle2,
  FileSpreadsheet,
  Users,
  Coins,
} from 'lucide-react';
import { TenantContextType } from '../types';

export const Dashboard: React.FC = () => {
  const { currentOrg, user } = useOutletContext<TenantContextType>();

  const currencySymbol =
    currentOrg.currency === 'PHP'
      ? '₱'
      : currentOrg.currency === 'EUR'
      ? '€'
      : currentOrg.currency === 'GBP'
      ? '£'
      : '$';

  return (
    <div className="space-y-6 animate-in fade-in duration-200 text-[var(--text-main)]">
      {/* Workspace Context Top Bar (Border-Less) */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 p-4 sm:p-5 rounded-2xl bg-gradient-to-r from-[var(--bg-surface)] via-[var(--bg-surface)] to-[var(--brand-subtle)] shadow-xl">
        <div className="space-y-1">
          <div className="flex items-center gap-2">
            <span className="w-2 h-2 rounded-full bg-[var(--brand-primary)] animate-pulse" />
            <span className="text-[10px] sm:text-xs font-semibold uppercase tracking-wider text-[var(--brand-primary)]">
              Active Tenant Workspace
            </span>
          </div>
          <h1 className="text-xl sm:text-2xl font-bold text-[var(--text-main)] tracking-tight">
            {currentOrg.whitelabel?.brandName ? `${currentOrg.name} (${currentOrg.whitelabel.brandName})` : currentOrg.name}
          </h1>
          <p className="text-xs text-[var(--text-muted)]">
            Welcome back, <span className="text-[var(--text-main)] font-medium">{user.name}</span>. Viewing organization-scoped financial analytics.
          </p>
        </div>

        {/* Tenant Meta Pill Badges */}
        <div className="flex flex-wrap items-center gap-2 sm:self-center">
          <div className="px-3 py-1.5 rounded-xl bg-[var(--bg-subtle)] shadow-sm text-xs">
            <span className="text-[var(--text-muted)]">Org ID: </span>
            <code className="text-[var(--text-secondary)] font-mono font-medium">{currentOrg.id}</code>
          </div>
          <div className="px-3 py-1.5 rounded-xl bg-[var(--bg-subtle)] shadow-sm text-xs">
            <span className="text-[var(--text-muted)]">Slug: </span>
            <code className="text-[var(--brand-primary)] font-mono font-medium">/org/{currentOrg.slug}</code>
          </div>
          <div className="px-3 py-1.5 rounded-xl bg-[var(--brand-subtle)] text-xs text-[var(--brand-primary)] font-medium">
            Role: {currentOrg.role || 'Admin'}
          </div>
        </div>
      </div>

      {/* Financial Metric Cards Grid (Border-Less with Ambient Elevation) */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 sm:gap-4">
        {/* Metric 1 */}
        <div className="p-4 sm:p-5 rounded-2xl bg-[var(--bg-surface)] hover:bg-[var(--bg-surface-hover)] backdrop-blur-md shadow-md hover:shadow-xl transition-all">
          <div className="flex items-center justify-between mb-3">
            <span className="text-xs font-semibold text-[var(--text-muted)]">Operating Revenue</span>
            <div className="w-8 h-8 rounded-xl bg-[var(--brand-subtle)] text-[var(--brand-primary)] flex items-center justify-center shadow-sm">
              <TrendingUp className="w-4 h-4" />
            </div>
          </div>
          <div className="text-xl sm:text-2xl font-bold text-[var(--text-main)] tracking-tight">
            {currencySymbol}128,450.00
          </div>
          <div className="flex items-center gap-1.5 text-xs text-[var(--brand-primary)] mt-2 font-semibold">
            <ArrowUpRight className="w-3.5 h-3.5" />
            <span>+14.2% vs last month</span>
          </div>
        </div>

        {/* Metric 2 */}
        <div className="p-4 sm:p-5 rounded-2xl bg-[var(--bg-surface)] hover:bg-[var(--bg-surface-hover)] backdrop-blur-md shadow-md hover:shadow-xl transition-all">
          <div className="flex items-center justify-between mb-3">
            <span className="text-xs font-semibold text-[var(--text-muted)]">Receivables (A/R)</span>
            <div className="w-8 h-8 rounded-xl bg-blue-500/10 text-blue-400 flex items-center justify-center shadow-sm">
              <Receipt className="w-4 h-4" />
            </div>
          </div>
          <div className="text-xl sm:text-2xl font-bold text-[var(--text-main)] tracking-tight">
            {currencySymbol}34,180.50
          </div>
          <div className="text-xs text-[var(--text-muted)] mt-2">
            6 open invoices awaiting settlement
          </div>
        </div>

        {/* Metric 3 */}
        <div className="p-4 sm:p-5 rounded-2xl bg-[var(--bg-surface)] hover:bg-[var(--bg-surface-hover)] backdrop-blur-md shadow-md hover:shadow-xl transition-all">
          <div className="flex items-center justify-between mb-3">
            <span className="text-xs font-semibold text-[var(--text-muted)]">Cash on Hand</span>
            <div className="w-8 h-8 rounded-xl bg-amber-500/10 text-amber-400 flex items-center justify-center shadow-sm">
              <Wallet className="w-4 h-4" />
            </div>
          </div>
          <div className="text-xl sm:text-2xl font-bold text-[var(--text-main)] tracking-tight">
            {currencySymbol}89,200.00
          </div>
          <div className="text-xs text-[var(--text-muted)] mt-2">
            Across 3 operational bank accounts
          </div>
        </div>

        {/* Metric 4 */}
        <div className="p-4 sm:p-5 rounded-2xl bg-[var(--bg-surface)] hover:bg-[var(--bg-surface-hover)] backdrop-blur-md shadow-md hover:shadow-xl transition-all">
          <div className="flex items-center justify-between mb-3">
            <span className="text-xs font-semibold text-[var(--text-muted)]">Active Ledger Accounts</span>
            <div className="w-8 h-8 rounded-xl bg-purple-500/10 text-purple-400 flex items-center justify-center shadow-sm">
              <BookOpen className="w-4 h-4" />
            </div>
          </div>
          <div className="text-xl sm:text-2xl font-bold text-[var(--text-main)] tracking-tight">
            42 Accounts
          </div>
          <div className="text-xs text-[var(--text-muted)] mt-2">
            Standard Chart of Accounts (COA)
          </div>
        </div>
      </div>

      {/* Multi-Tenant Deep Linking & Isolation Note */}
      <div className="p-4 rounded-xl bg-[var(--bg-subtle)] shadow-inner flex items-start gap-3 text-xs text-[var(--text-muted)]">
        <ShieldCheck className="w-4 h-4 text-[var(--brand-primary)] flex-shrink-0 mt-0.5" />
        <div className="space-y-1">
          <div className="font-semibold text-[var(--text-main)]">
            Tenant State Guarantee: Embedded URL Slug Architecture
          </div>
          <p className="leading-relaxed">
            Every request, bookmark, and state query is bound to{' '}
            <code className="text-[var(--brand-primary)] font-mono font-medium">
              /org/{currentOrg.slug}/...
            </code>
            . Refreshing the browser or deep-linking to sub-routes retains full isolation of tenant accounting records without cross-tenant session leaks.
          </p>
        </div>
      </div>

      {/* Quick Actions & Recent Activity Skeleton */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        {/* Left 2 Cols: Activity Feed */}
        <div className="lg:col-span-2 space-y-4">
          <div className="flex items-center justify-between">
            <h2 className="text-sm sm:text-base font-bold text-[var(--text-main)] flex items-center gap-2">
              <FileSpreadsheet className="w-4 h-4 text-[var(--brand-primary)]" />
              <span>Recent General Ledger Entries</span>
            </h2>
            <Link
              to={`/org/${currentOrg.slug}/financials`}
              className="text-xs font-semibold text-[var(--brand-primary)] hover:opacity-80 transition-opacity"
            >
              View Full Journal →
            </Link>
          </div>

          <div className="rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md overflow-hidden">
            <div className="overflow-x-auto smooth-scroll">
              <table className="w-full min-w-[540px] text-left text-xs">
                <thead className="bg-[var(--bg-subtle)] text-[var(--text-muted)] uppercase tracking-wider text-[10px] font-semibold">
                  <tr>
                    <th className="px-4 py-3">Reference</th>
                    <th className="px-4 py-3">Account Description</th>
                    <th className="px-4 py-3">Type</th>
                    <th className="px-4 py-3 text-right">Debit</th>
                    <th className="px-4 py-3 text-right">Credit</th>
                  </tr>
                </thead>
                <tbody className="text-[var(--text-secondary)] font-mono divide-y divide-[var(--bg-subtle)]">
                  <tr className="hover:bg-[var(--bg-surface-hover)] transition-colors">
                    <td className="px-4 py-3.5 text-[var(--brand-primary)] font-medium">JV-2026-0041</td>
                    <td className="px-4 py-3.5 font-sans text-[var(--text-main)]">Accounts Receivable / Sales Revenue</td>
                    <td className="px-4 py-3.5 font-sans">
                      <span className="px-2 py-0.5 rounded-full bg-[var(--brand-subtle)] text-[var(--brand-primary)] text-[10px] uppercase font-semibold">
                        Invoice
                      </span>
                    </td>
                    <td className="px-4 py-3.5 text-right font-semibold text-[var(--text-main)]">{currencySymbol}14,500.00</td>
                    <td className="px-4 py-3.5 text-right text-[var(--text-muted)]">—</td>
                  </tr>
                  <tr className="hover:bg-[var(--bg-surface-hover)] transition-colors">
                    <td className="px-4 py-3.5 text-[var(--brand-primary)] font-medium">JV-2026-0040</td>
                    <td className="px-4 py-3.5 font-sans text-[var(--text-main)]">Office & Software Subscription Expense</td>
                    <td className="px-4 py-3.5 font-sans">
                      <span className="px-2 py-0.5 rounded-full bg-blue-500/10 text-blue-400 text-[10px] uppercase font-semibold">
                        Expense
                      </span>
                    </td>
                    <td className="px-4 py-3.5 text-right font-semibold text-[var(--text-main)]">{currencySymbol}1,250.00</td>
                    <td className="px-4 py-3.5 text-right text-[var(--text-muted)]">—</td>
                  </tr>
                  <tr className="hover:bg-[var(--bg-surface-hover)] transition-colors">
                    <td className="px-4 py-3.5 text-[var(--brand-primary)] font-medium">JV-2026-0039</td>
                    <td className="px-4 py-3.5 font-sans text-[var(--text-main)]">Cash Settlement / Bank Transfer</td>
                    <td className="px-4 py-3.5 font-sans">
                      <span className="px-2 py-0.5 rounded-full bg-amber-500/10 text-amber-400 text-[10px] uppercase font-semibold">
                        Payment
                      </span>
                    </td>
                    <td className="px-4 py-3.5 text-right text-[var(--text-muted)]">—</td>
                    <td className="px-4 py-3.5 text-right font-semibold text-[var(--text-main)]">{currencySymbol}5,000.00</td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        </div>

        {/* Right 1 Col: Quick Actions & Team */}
        <div className="space-y-4">
          <h2 className="text-sm sm:text-base font-bold text-[var(--text-main)] flex items-center gap-2">
            <Plus className="w-4 h-4 text-[var(--brand-primary)]" />
            <span>Workspace Actions</span>
          </h2>

          <div className="p-3 sm:p-4 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-2">
            <button
              type="button"
              className="w-full min-h-[44px] flex items-center justify-between p-3 rounded-xl bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)] text-left text-xs font-semibold text-[var(--text-main)] transition-all shadow-sm active:scale-[0.98] cursor-pointer"
            >
              <span className="flex items-center gap-2.5">
                <Receipt className="w-4 h-4 text-[var(--brand-primary)] shrink-0" />
                <span>Create Sales Invoice</span>
              </span>
              <span className="text-[10px] text-[var(--text-muted)] font-mono">⌘N</span>
            </button>

            <button
              type="button"
              className="w-full min-h-[44px] flex items-center justify-between p-3 rounded-xl bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)] text-left text-xs font-semibold text-[var(--text-main)] transition-all shadow-sm active:scale-[0.98] cursor-pointer"
            >
              <span className="flex items-center gap-2.5">
                <Coins className="w-4 h-4 text-blue-400 shrink-0" />
                <span>Post Journal Voucher</span>
              </span>
              <span className="text-[10px] text-[var(--text-muted)] font-mono">⌘J</span>
            </button>

            <Link
              to={`/org/${currentOrg.slug}/settings`}
              className="w-full min-h-[44px] flex items-center justify-between p-3 rounded-xl bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)] text-left text-xs font-semibold text-[var(--text-main)] transition-all shadow-sm active:scale-[0.98]"
            >
              <span className="flex items-center gap-2.5">
                <Users className="w-4 h-4 text-purple-400 shrink-0" />
                <span>Manage Tenant Members</span>
              </span>
              <span className="text-[var(--text-muted)] text-xs">→</span>
            </Link>
          </div>

          {/* Quick Tenant Info Box */}
          <div className="p-4 rounded-2xl bg-[var(--brand-subtle)] shadow-md text-xs space-y-2">
            <div className="flex items-center gap-2 text-[var(--brand-primary)] font-bold">
              <CheckCircle2 className="w-4 h-4" />
              <span>Multi-Tenant Skeleton Ready</span>
            </div>
            <p className="text-[var(--text-muted)] text-[11px] leading-relaxed">
              Use the organization switcher in the sidebar or top breadcrumbs to test switching across tenants in real-time.
            </p>
          </div>
        </div>
      </div>
    </div>
  );
};

export default Dashboard;
