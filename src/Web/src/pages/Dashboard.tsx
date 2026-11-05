import React, { useState, useEffect, useRef } from 'react';
import { useOutletContext, Link } from 'react-router-dom';
import {
  TrendingUp,
  Receipt,
  Wallet,
  BookOpen,
  ArrowUpRight,
  ArrowDownRight,
  Plus,
  FileSpreadsheet,
  Users,
  Coins,
  Clock,
  Loader2,
  AlertTriangle,
  ShoppingCart,
  Package,
} from 'lucide-react';
import { TenantContextType } from '../types';
import { useAuth } from '../auth/AuthContext';
import { currencySymbol as getCurrencySymbol } from '../lib/currency';
import { OnboardingChecklist } from '../components/OnboardingChecklist';
import { BankCloseStatusCard } from '../components/BankCloseStatusCard';
import { MoneyInBankBreakdown } from '../components/MoneyInBankBreakdown';
import { PersonalFinanceHealthCard } from '../components/PersonalFinanceHealthCard';
import {
  isPersonalWorkspace,
  resolveDashboardSurface,
  type DashboardCapabilities,
} from '../lib/dashboardProduct';
import type { DashboardBankBalance } from '../lib/moneyInBank';
import type { BankCloseStatus } from '../types/banking';

interface DashboardSummary {
  surface?: 'Accounting' | 'Invoicing' | string;
  capabilities?: DashboardCapabilities;
  kpis: {
    moneyInBank: number;
    bankBalances?: DashboardBankBalance[] | null;
    unassignedCash?: number;
    outstandingReceivables: number;
    totalPayables: number;
    totalAssets: number;
    totalSalesYtd: number;
    totalExpensesYtd: number;
    netProfitYtd: number;
    earnedIncomeYtd?: number;
    moneyFromFreelanceWorkYtd?: number;
    drawingsYtd?: number;
    cashFlowYtd: number;
    overdueReceivables?: number;
    openInvoiceCount?: number;
    overdueInvoiceCount?: number;
  };
  balanceSnapshot: {
    totalAssets: number;
    totalLiabilities: number;
    totalEquity: number;
  };
  monthlySales: Array<{ label: string; amount: number }>;
  monthlyExpenses: Array<{ label: string; amount: number }>;
  topReceivables: Array<{
    customerId: number;
    customerName: string;
    balance: number;
  }>;
  upcomingPayables: Array<{
    purchaseInvoiceId: number;
    vendorName: string;
    invoiceNo: string;
    remainingAmount: number;
    invoiceDate: string;
  }>;
  recentActivity: Array<{
    type: string;
    description: string;
    date: string;
  }>;
  bankCloseStatus?: BankCloseStatus | null;
}

interface LinkedWorkspaceInfo { otherWorkspace?: { name: string; slug: string } | null }

interface YearEndYearRow {
  fiscalYearCode: string;
  endDate: string;
  status: number;
}

export const Dashboard: React.FC = () => {
  const { currentOrg, user, hasModule, hasPermission } = useOutletContext<TenantContextType>();
  const { authFetch } = useAuth();

  const [summary, setSummary] = useState<DashboardSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [yearEndReminder, setYearEndReminder] = useState<YearEndYearRow | null>(null);
  const [reloadToken, setReloadToken] = useState(0);
  const [linkedWorkspace, setLinkedWorkspace] = useState<LinkedWorkspaceInfo['otherWorkspace']>(null);

  const currencySymbol = getCurrencySymbol(currentOrg.currency);
  const hasFinancialsModule = hasModule('financials');
  const personal = isPersonalWorkspace(currentOrg);
  const soleProprietor = !personal && currentOrg.entityType === 'SoleProprietor';
  const caps = summary?.capabilities;
  const surface = resolveDashboardSurface(summary?.surface, hasFinancialsModule);
  const invoicingSurface = surface === 'Invoicing';
  const accountingSurface = surface === 'Accounting';
  // Prefer server-authorized capabilities once loaded; until then avoid claiming accounting cards.
  const canShowFinancials = caps ? Boolean(caps.financials) : false;
  const canShowSales = caps ? Boolean(caps.sales) : hasModule('sales');
  const canShowPurchases = caps ? Boolean(caps.purchases) : hasModule('purchases');
  const moneyInBankAvailable = Boolean(caps?.moneyInBank);
  const receivablesAvailable = Boolean(caps?.receivables);
  const payablesAvailable = Boolean(caps?.payables);
  const workspaceSlugRef = useRef(currentOrg.slug);
  workspaceSlugRef.current = currentOrg.slug;

  useEffect(() => {
    let cancelled = false;
    const requestedSlug = currentOrg.slug;
    // Drop the prior workspace immediately so a late body cannot paint over the next one.
    setSummary(null);
    setLoading(true);
    setLoadError(null);

    const stillForThisWorkspace = () =>
      !cancelled && workspaceSlugRef.current === requestedSlug;

    const fetchSummary = async () => {
      try {
        const res = await authFetch('/api/Dashboard/Summary');
        if (!stillForThisWorkspace()) return;
        if (res.ok) {
          const body = (await res.json()) as DashboardSummary;
          // Re-check after parse — res.json() can resolve after a workspace switch.
          if (!stillForThisWorkspace()) return;
          setSummary(body);
          setLoadError(null);
        } else {
          setSummary(null);
          setLoadError(`Could not load dashboard (${res.status}).`);
        }
      } catch (err) {
        console.error('Failed to load dashboard summary:', err);
        if (stillForThisWorkspace()) {
          setSummary(null);
          setLoadError(err instanceof Error ? err.message : 'Could not load dashboard.');
        }
      } finally {
        if (stillForThisWorkspace()) setLoading(false);
      }
    };

    void fetchSummary();
    return () => {
      cancelled = true;
    };
  }, [authFetch, currentOrg.slug, reloadToken]);

  useEffect(() => {
    let mounted = true;
    setLinkedWorkspace(null);
    void authFetch('/api/linked-entries').then(async (res) => {
      if (!res.ok) return;
      const body = (await res.json()) as LinkedWorkspaceInfo;
      if (mounted) setLinkedWorkspace(body.otherWorkspace ?? null);
    }).catch(() => { if (mounted) setLinkedWorkspace(null); });
    return () => { mounted = false; };
  }, [authFetch, currentOrg.slug]);

  useEffect(() => {
    if (!canShowFinancials || !hasPermission('financials.periods.close')) {
      setYearEndReminder(null);
      return;
    }
    let mounted = true;
    void (async () => {
      try {
        const res = await authFetch('/api/Financials/YearEnd');
        if (!res.ok || !mounted) return;
        const years = (await res.json()) as YearEndYearRow[];
        const ended = years
          .filter((y) => y.status === 2)
          .sort((a, b) => b.endDate.localeCompare(a.endDate));
        setYearEndReminder(ended[0] ?? null);
      } catch {
        if (mounted) setYearEndReminder(null);
      }
    })();
    return () => {
      mounted = false;
    };
  }, [authFetch, currentOrg.slug, canShowFinancials, hasPermission]);

  const kpis = summary?.kpis;
  const recentActivity = summary?.recentActivity || [];
  const topReceivables = summary?.topReceivables || [];
  const upcomingPayables = summary?.upcomingPayables || [];

  const formatAmount = (val: number | undefined) => {
    if (val === undefined || isNaN(val)) return '0.00';
    return Number(val).toLocaleString(undefined, {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    });
  };

  const formatCurrencyValue = (val: number) => {
    const formatted = Math.abs(val).toLocaleString(undefined, {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    });
    return val < 0 ? `-${currencySymbol}${formatted}` : `${currencySymbol}${formatted}`;
  };

  /** Loaded authorized amounts only — never paint restricted/missing data as a healthy zero. */
  const formatMetric = (val: number | null | undefined, available: boolean) => {
    if (!available) return 'Unavailable';
    if (val === null || val === undefined || Number.isNaN(val)) return 'Unavailable';
    return formatCurrencyValue(val);
  };

  const formatDate = (isoStr: string) => {
    try {
      const d = new Date(isoStr);
      return d.toLocaleDateString(undefined, {
        month: 'short',
        day: 'numeric',
        year: 'numeric',
      });
    } catch {
      return isoStr;
    }
  };

  const welcomeLine = personal
    ? invoicingSurface
      ? 'Here is your personal billing and collections overview.'
      : 'Here is your personal finance overview from the books.'
    : invoicingSurface
      ? 'Here is your invoicing overview — billing, collections, and bills.'
      : "Here is an overview of your organization's financials.";

  return (
    <div className="space-y-6 animate-in fade-in duration-200 text-[var(--text-main)]">
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 p-4 sm:p-5 rounded-2xl bg-gradient-to-r from-[var(--bg-surface)] via-[var(--bg-surface)] to-[var(--brand-subtle)] shadow-xl">
        <div className="space-y-1">
          <div className="flex items-center gap-2">
            <span className="w-2 h-2 rounded-full bg-[var(--brand-primary)]" />
            <span className="text-[10px] sm:text-xs font-semibold uppercase tracking-wider text-[var(--brand-primary)]">
              {invoicingSurface ? 'Invoicing workspace' : 'Accounting workspace'}
              {personal ? ' · Personal' : ''}
            </span>
          </div>
          <h1 className="text-xl sm:text-2xl font-bold text-[var(--text-main)] tracking-tight">
            {currentOrg.whitelabel?.brandName ? `${currentOrg.name} (${currentOrg.whitelabel.brandName})` : currentOrg.name}
          </h1>
          <p className="text-xs text-[var(--text-muted)]">
            Welcome back, <span className="text-[var(--text-main)] font-medium">{user.name}</span>. {welcomeLine}
          </p>
        </div>

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
            Plan: {currentOrg.plan ? currentOrg.plan.toUpperCase() : 'ACCOUNTING'}
          </div>
        </div>
      </div>

      <OnboardingChecklist
        orgSlug={currentOrg.slug}
        hasFinancialsModule={hasFinancialsModule}
        canManage={currentOrg.role === 'Owner' || currentOrg.role === 'Admin'}
      />

      {linkedWorkspace && (
        <Link to={`/org/${linkedWorkspace.slug}/dashboard`} className="inline-flex items-center rounded-full border border-[var(--brand-primary)]/20 bg-[var(--brand-subtle)] px-3 py-1 text-xs font-medium text-[var(--brand-primary)]" data-testid="linked-workspace-chip">
          Linked to {linkedWorkspace.name}
        </Link>
      )}

      {personal && canShowFinancials && !loadError && (
        <section aria-label="Money in and out" className="grid grid-cols-1 sm:grid-cols-3 gap-3" data-testid="personal-money-in-out">
          <h2 className="sm:col-span-3 text-sm font-bold text-[var(--text-main)]">Money in and out</h2>
          <div><span className="text-xs text-[var(--text-muted)]">Earned income</span><strong className="block">{formatMetric(kpis?.earnedIncomeYtd, true)}</strong></div>
          <div><span className="text-xs text-[var(--text-muted)]">Money from my freelance work</span><strong className="block">{formatMetric(kpis?.moneyFromFreelanceWorkYtd, true)}</strong></div>
          <div><span className="text-xs text-[var(--text-muted)]">Money out</span><strong className="block">{formatMetric(kpis?.totalExpensesYtd, true)}</strong></div>
        </section>
      )}

      {loadError && (
        <div
          role="alert"
          className="p-4 rounded-2xl bg-rose-500/10 text-rose-400 text-xs font-semibold flex flex-wrap items-center justify-between gap-3"
          data-testid="dashboard-load-error"
        >
          <span>{loadError}</span>
          <button
            type="button"
            className="px-3 py-1.5 rounded-xl bg-[var(--bg-subtle)] text-[var(--text-main)] font-semibold"
            onClick={() => setReloadToken((n) => n + 1)}
          >
            Retry
          </button>
        </div>
      )}

      {personal && !loadError && (
        <PersonalFinanceHealthCard
          loading={loading}
          hasFinancials={canShowFinancials}
          currencyFormat={formatMetric}
          moneyInBank={kpis?.moneyInBank}
          moneyInBankAvailable={moneyInBankAvailable}
          outstandingReceivables={kpis?.outstandingReceivables}
          receivablesAvailable={receivablesAvailable}
          totalPayables={kpis?.totalPayables}
          payablesAvailable={payablesAvailable}
          netWorth={summary?.balanceSnapshot?.totalEquity}
          netWorthAvailable={canShowFinancials}
        />
      )}

      {accountingSurface && canShowFinancials && (
        <BankCloseStatusCard status={summary?.bankCloseStatus} orgSlug={currentOrg.slug} />
      )}

      {yearEndReminder && (
        <p className="text-sm text-amber-700 bg-amber-500/10 px-4 py-3 rounded-2xl">
          Fiscal year {yearEndReminder.fiscalYearCode} has ended and is ready to close.{' '}
          <Link
            to={`/org/${currentOrg.slug}/settings?tab=financials`}
            className="font-semibold text-[var(--brand-primary)] hover:underline"
          >
            Open year-end close in Settings
          </Link>
        </p>
      )}

      {/* KPI grid — product-shaped */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 sm:gap-4">
        <div className="p-4 sm:p-5 rounded-2xl bg-[var(--bg-surface)] hover:bg-[var(--bg-surface-hover)] backdrop-blur-md shadow-md hover:shadow-xl transition-all">
          <div className="flex items-center justify-between mb-3">
            <span className="text-xs font-semibold text-[var(--text-muted)]">
              {personal ? 'Earned income' : soleProprietor ? 'Money in' : invoicingSurface ? 'Invoiced (YTD)' : 'Operating Sales (YTD)'}
            </span>
            <div className="w-8 h-8 rounded-xl bg-[var(--brand-subtle)] text-[var(--brand-primary)] flex items-center justify-center shadow-sm">
              <TrendingUp className="w-4 h-4" />
            </div>
          </div>
          <div className="text-xl sm:text-2xl font-bold text-[var(--text-main)] tracking-tight">
            {loading ? (
              <span className="inline-block w-28 h-7 bg-[var(--bg-subtle)] rounded animate-pulse" />
            ) : (
              formatMetric(
                personal ? kpis?.earnedIncomeYtd : soleProprietor ? kpis?.moneyInBank : kpis?.totalSalesYtd,
                invoicingSurface ? canShowSales : canShowFinancials,
              )
            )}
          </div>
          {invoicingSurface ? (
            <div className="text-xs text-[var(--text-muted)] mt-2">
              Issued invoice totals incl. tax and shipping; excludes drafts and voids. Not cash collected or profit.
              {canShowSales
                ? (kpis?.openInvoiceCount ?? 0) === 1
                  ? ' · 1 open invoice'
                  : ` · ${kpis?.openInvoiceCount ?? 0} open invoices`
                : ''}
            </div>
          ) : soleProprietor ? null : canShowFinancials ? (
            <div
              className={`flex items-center gap-1.5 text-xs mt-2 font-semibold ${
                (kpis?.netProfitYtd ?? 0) >= 0 ? 'text-[var(--brand-primary)]' : 'text-rose-500'
              }`}
            >
              {(kpis?.netProfitYtd ?? 0) >= 0 ? (
                <ArrowUpRight className="w-3.5 h-3.5" />
              ) : (
                <ArrowDownRight className="w-3.5 h-3.5" />
              )}
              <span>
                {(kpis?.netProfitYtd ?? 0) >= 0 ? 'Net profit: ' : 'Net loss: '}
                {formatCurrencyValue(Math.abs(kpis?.netProfitYtd ?? 0))}
              </span>
            </div>
          ) : (
            <div className="text-xs text-[var(--text-muted)] mt-2">Ledger figures require financials access.</div>
          )}
        </div>

        <div className="p-4 sm:p-5 rounded-2xl bg-[var(--bg-surface)] hover:bg-[var(--bg-surface-hover)] backdrop-blur-md shadow-md hover:shadow-xl transition-all">
          <div className="flex items-center justify-between mb-3">
            <span className="text-xs font-semibold text-[var(--text-muted)]">
              {personal ? 'Owed to you' : soleProprietor ? 'Money out' : 'Receivables (A/R)'}
            </span>
            <div className="w-8 h-8 rounded-xl bg-blue-500/10 text-blue-400 flex items-center justify-center shadow-sm">
              <Receipt className="w-4 h-4" />
            </div>
          </div>
          <div className="text-xl sm:text-2xl font-bold text-[var(--text-main)] tracking-tight">
            {loading ? (
              <span className="inline-block w-28 h-7 bg-[var(--bg-subtle)] rounded animate-pulse" />
            ) : (
              formatMetric(soleProprietor ? kpis?.totalExpensesYtd : kpis?.outstandingReceivables, soleProprietor ? canShowFinancials : receivablesAvailable)
            )}
          </div>
          <div className="text-xs text-[var(--text-muted)] mt-2">
            {soleProprietor ? 'Expenses for this period' : !receivablesAvailable
              ? 'Requires sales access'
              : (kpis?.outstandingReceivables ?? 0) > 0
                ? 'Open invoices incl. tax, shipping, and discounts'
                : 'No outstanding balances'}
          </div>
        </div>

        {invoicingSurface ? (
          <div className="p-4 sm:p-5 rounded-2xl bg-[var(--bg-surface)] hover:bg-[var(--bg-surface-hover)] backdrop-blur-md shadow-md hover:shadow-xl transition-all">
            <div className="flex items-center justify-between mb-3">
              <span className="text-xs font-semibold text-[var(--text-muted)]">Overdue to collect</span>
              <div className="w-8 h-8 rounded-xl bg-rose-500/10 text-rose-400 flex items-center justify-center shadow-sm">
                <AlertTriangle className="w-4 h-4" />
              </div>
            </div>
            <div className="text-xl sm:text-2xl font-bold text-[var(--text-main)] tracking-tight">
              {loading ? (
                <span className="inline-block w-28 h-7 bg-[var(--bg-subtle)] rounded animate-pulse" />
              ) : (
                formatMetric(kpis?.overdueReceivables, receivablesAvailable)
              )}
            </div>
            <div className="text-xs text-[var(--text-muted)] mt-2">
              {!receivablesAvailable
                ? 'Requires sales access'
                : (kpis?.overdueInvoiceCount ?? 0) === 0
                  ? 'Nothing past due'
                  : `${kpis?.overdueInvoiceCount} past due`}
            </div>
          </div>
        ) : (
          <div className="p-4 sm:p-5 rounded-2xl bg-[var(--bg-surface)] hover:bg-[var(--bg-surface-hover)] backdrop-blur-md shadow-md hover:shadow-xl transition-all">
            <div className="flex items-center justify-between mb-3">
              <span className="text-xs font-semibold text-[var(--text-muted)]">{soleProprietor ? 'Taken for personal use' : 'Cash & Bank Balances'}</span>
              <div className="w-8 h-8 rounded-xl bg-amber-500/10 text-amber-400 flex items-center justify-center shadow-sm">
                <Wallet className="w-4 h-4" />
              </div>
            </div>
            <div className="text-xl sm:text-2xl font-bold text-[var(--text-main)] tracking-tight">
              {loading ? (
                <span className="inline-block w-28 h-7 bg-[var(--bg-subtle)] rounded animate-pulse" />
              ) : (
                formatMetric(soleProprietor ? kpis?.drawingsYtd : kpis?.moneyInBank, soleProprietor ? canShowFinancials : moneyInBankAvailable)
              )}
            </div>
            <div className="text-xs text-[var(--text-muted)] mt-2">
              {soleProprietor ? 'Owner drawings for this period' : moneyInBankAvailable
                ? 'Available across cash and bank accounts'
                : 'Requires banking or financials access'}
            </div>
            {!soleProprietor && !loading && moneyInBankAvailable && (
              <MoneyInBankBreakdown
                kpis={kpis}
                orgSlug={currentOrg.slug}
                currencySymbol={currencySymbol}
                formatAmount={formatAmount}
              />
            )}
          </div>
        )}

        {invoicingSurface ? (
          <div className="p-4 sm:p-5 rounded-2xl bg-[var(--bg-surface)] hover:bg-[var(--bg-surface-hover)] backdrop-blur-md shadow-md hover:shadow-xl transition-all">
            <div className="flex items-center justify-between mb-3">
              <span className="text-xs font-semibold text-[var(--text-muted)]">
                {personal ? 'You owe' : 'Bills to pay (A/P)'}
              </span>
              <div className="w-8 h-8 rounded-xl bg-amber-500/10 text-amber-400 flex items-center justify-center shadow-sm">
                <ShoppingCart className="w-4 h-4" />
              </div>
            </div>
            <div className="text-xl sm:text-2xl font-bold text-[var(--text-main)] tracking-tight">
              {loading ? (
                <span className="inline-block w-28 h-7 bg-[var(--bg-subtle)] rounded animate-pulse" />
              ) : (
                formatMetric(kpis?.totalPayables, payablesAvailable)
              )}
            </div>
            <div className="text-xs text-[var(--text-muted)] mt-2">
              {!payablesAvailable
                ? 'Requires purchases access'
                : (kpis?.totalPayables ?? 0) > 0
                  ? 'Open purchase bills'
                  : 'No open bills'}
            </div>
          </div>
        ) : (
          <div className="p-4 sm:p-5 rounded-2xl bg-[var(--bg-surface)] hover:bg-[var(--bg-surface-hover)] backdrop-blur-md shadow-md hover:shadow-xl transition-all">
            <div className="flex items-center justify-between mb-3">
              <span className="text-xs font-semibold text-[var(--text-muted)]">
                {personal ? 'Net worth (book value)' : soleProprietor && (kpis?.earnedIncomeYtd ?? 0) > 0 ? 'Work profit' : 'Total Assets'}
              </span>
              <div className="w-8 h-8 rounded-xl bg-purple-500/10 text-purple-400 flex items-center justify-center shadow-sm">
                <BookOpen className="w-4 h-4" />
              </div>
            </div>
            <div className="text-xl sm:text-2xl font-bold text-[var(--text-main)] tracking-tight">
              {loading ? (
                <span className="inline-block w-28 h-7 bg-[var(--bg-subtle)] rounded animate-pulse" />
              ) : personal ? (
                formatMetric(summary?.balanceSnapshot?.totalEquity, canShowFinancials)
              ) : soleProprietor && (kpis?.earnedIncomeYtd ?? 0) > 0 ? (
                formatMetric(kpis?.netProfitYtd, canShowFinancials)
              ) : (
                formatMetric(kpis?.totalAssets, canShowFinancials)
              )}
            </div>
            <div className="text-xs text-[var(--text-muted)] mt-2">
              {!canShowFinancials
                ? 'Requires financials access'
                : personal
                  ? `Assets ${formatMetric(summary?.balanceSnapshot?.totalAssets, true)} · Liabilities ${formatMetric(summary?.balanceSnapshot?.totalLiabilities, true)}`
                  : `Total liabilities: ${formatMetric(summary?.balanceSnapshot?.totalLiabilities, true)}`}
            </div>
          </div>
        )}
      </div>

      {invoicingSurface && moneyInBankAvailable && !loadError && (
        <div className="p-4 sm:p-5 rounded-2xl bg-[var(--bg-surface)] shadow-md">
          <div className="flex items-center justify-between mb-2">
            <h2 className="text-sm font-bold text-[var(--text-main)] flex items-center gap-2">
              <Wallet className="w-4 h-4 text-amber-400" />
              Cash &amp; bank
            </h2>
            <span className="text-sm font-bold text-[var(--text-main)]">
              {formatMetric(kpis?.moneyInBank, moneyInBankAvailable)}
            </span>
          </div>
          {!loading && (
            <MoneyInBankBreakdown
              kpis={kpis}
              orgSlug={currentOrg.slug}
              currencySymbol={currencySymbol}
              formatAmount={formatAmount}
            />
          )}
        </div>
      )}

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        <div className="lg:col-span-2 space-y-4">
          <div className="flex items-center justify-between">
            <h2 className="text-sm sm:text-base font-bold text-[var(--text-main)] flex items-center gap-2">
              <FileSpreadsheet className="w-4 h-4 text-[var(--brand-primary)]" />
              <span>{invoicingSurface ? 'Recent invoices & bills' : 'Recent Activity & Entries'}</span>
            </h2>
            {invoicingSurface ? (
              canShowSales ? (
                <Link
                  to={`/org/${currentOrg.slug}/sales`}
                  className="text-xs font-semibold text-[var(--brand-primary)] hover:opacity-80 transition-opacity"
                >
                  View sales →
                </Link>
              ) : canShowPurchases ? (
                <Link
                  to={`/org/${currentOrg.slug}/purchases`}
                  className="text-xs font-semibold text-[var(--brand-primary)] hover:opacity-80 transition-opacity"
                >
                  View purchases →
                </Link>
              ) : null
            ) : canShowFinancials ? (
              <Link
                to={`/org/${currentOrg.slug}/financials`}
                className="text-xs font-semibold text-[var(--brand-primary)] hover:opacity-80 transition-opacity"
              >
                View Full Accounts →
              </Link>
            ) : null}
          </div>

          <div className="rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md overflow-hidden">
            <div className="overflow-x-auto smooth-scroll">
              <table
                className="w-full min-w-[540px] text-left text-xs"
                aria-label={invoicingSurface ? 'Recent invoices and bills' : 'Recent entries and journal transactions'}
              >
                <thead className="bg-[var(--bg-subtle)] text-[var(--text-muted)] uppercase tracking-wider text-[10px] font-semibold">
                  <tr>
                    <th scope="col" className="px-4 py-3">Type</th>
                    <th scope="col" className="px-4 py-3">Description</th>
                    <th scope="col" className="px-4 py-3 text-right">Date</th>
                  </tr>
                </thead>
                <tbody className="text-[var(--text-secondary)] font-mono divide-y divide-[var(--bg-subtle)]">
                  {loading ? (
                    <tr>
                      <td colSpan={3} className="p-8 text-center text-[var(--text-muted)]">
                        <Loader2 className="w-5 h-5 mx-auto animate-spin mb-2" />
                        Loading recent activity...
                      </td>
                    </tr>
                  ) : recentActivity.length === 0 ? (
                    <tr>
                      <td colSpan={3} className="p-8 text-center text-[var(--text-muted)] font-sans">
                        {invoicingSurface
                          ? 'No invoices or bills yet. Create your first sales invoice to get started.'
                          : 'No recent activity recorded for this period.'}
                      </td>
                    </tr>
                  ) : (
                    recentActivity.map((item, idx) => (
                      <tr key={idx} className="hover:bg-[var(--bg-surface-hover)] transition-colors">
                        <td className="px-4 py-3.5 font-sans">
                          <span
                            className={`px-2 py-0.5 rounded-full text-[10px] uppercase font-semibold ${
                              item.type.includes('Sales')
                                ? 'bg-[var(--brand-subtle)] text-[var(--brand-primary)]'
                                : item.type.includes('Purchase')
                                ? 'bg-blue-500/10 text-blue-400'
                                : 'bg-amber-500/10 text-amber-400'
                            }`}
                          >
                            {item.type}
                          </span>
                        </td>
                        <td className="px-4 py-3.5 font-sans text-[var(--text-main)] font-medium">
                          {item.description}
                        </td>
                        <td className="px-4 py-3.5 text-right text-[var(--text-muted)] font-sans">
                          {formatDate(item.date)}
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
          </div>

          {topReceivables.length > 0 && (
            <div className="p-4 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-3">
              <h3 className="text-xs font-bold text-[var(--text-main)] uppercase tracking-wider flex items-center gap-2">
                <Receipt className="w-3.5 h-3.5 text-[var(--brand-primary)]" />
                <span>{personal ? 'Who owes you' : 'Top Customer Balances (A/R)'}</span>
              </h3>
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                {topReceivables.map((cust) => (
                  <div
                    key={cust.customerId}
                    className="p-3 rounded-xl bg-[var(--bg-subtle)] flex items-center justify-between text-xs"
                  >
                    <div>
                      <div className="font-semibold text-[var(--text-main)]">{cust.customerName}</div>
                      <div className="text-[10px] text-[var(--text-muted)]">Customer #{cust.customerId}</div>
                    </div>
                    <div className="font-mono font-bold text-[var(--brand-primary)]">
                      {formatMetric(cust.balance, receivablesAvailable)}
                    </div>
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>

        <div className="space-y-4">
          <h2 className="text-sm sm:text-base font-bold text-[var(--text-main)] flex items-center gap-2">
            <Plus className="w-4 h-4 text-[var(--brand-primary)]" />
            <span>Workspace Actions</span>
          </h2>

          <div className="p-3 sm:p-4 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-2">
            {hasModule('sales') && canShowSales && (
              <Link
                to={`/org/${currentOrg.slug}/sales`}
                className="w-full min-h-[44px] flex items-center justify-between p-3 rounded-xl bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)] text-left text-xs font-semibold text-[var(--text-main)] transition-all shadow-sm active:scale-[0.98] cursor-pointer"
              >
                <span className="flex items-center gap-2.5">
                  <Receipt className="w-4 h-4 text-[var(--brand-primary)] shrink-0" />
                  <span>Sales Invoices</span>
                </span>
                <span className="text-[10px] text-[var(--text-muted)] font-mono">→</span>
              </Link>
            )}

            {hasModule('purchases') && canShowPurchases && (
              <Link
                to={`/org/${currentOrg.slug}/purchases`}
                className="w-full min-h-[44px] flex items-center justify-between p-3 rounded-xl bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)] text-left text-xs font-semibold text-[var(--text-main)] transition-all shadow-sm active:scale-[0.98] cursor-pointer"
              >
                <span className="flex items-center gap-2.5">
                  <ShoppingCart className="w-4 h-4 text-amber-400 shrink-0" />
                  <span>Bills &amp; Purchases</span>
                </span>
                <span className="text-[10px] text-[var(--text-muted)] font-mono">→</span>
              </Link>
            )}

            {hasModule('sales') && canShowSales && (
              <Link
                to={`/org/${currentOrg.slug}/items`}
                className="w-full min-h-[44px] flex items-center justify-between p-3 rounded-xl bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)] text-left text-xs font-semibold text-[var(--text-main)] transition-all shadow-sm active:scale-[0.98] cursor-pointer"
              >
                <span className="flex items-center gap-2.5">
                  <Package className="w-4 h-4 text-blue-400 shrink-0" />
                  <span>Products &amp; services</span>
                </span>
                <span className="text-[10px] text-[var(--text-muted)] font-mono">→</span>
              </Link>
            )}

            {hasFinancialsModule && canShowFinancials && (
              <Link
                to={`/org/${currentOrg.slug}/financials`}
                className="w-full min-h-[44px] flex items-center justify-between p-3 rounded-xl bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)] text-left text-xs font-semibold text-[var(--text-main)] transition-all shadow-sm active:scale-[0.98] cursor-pointer"
              >
                <span className="flex items-center gap-2.5">
                  <Coins className="w-4 h-4 text-blue-400 shrink-0" />
                  <span>General Ledger Accounts</span>
                </span>
                <span className="text-[10px] text-[var(--text-muted)] font-mono">→</span>
              </Link>
            )}

            <Link
              to={`/org/${currentOrg.slug}/settings`}
              className="w-full min-h-[44px] flex items-center justify-between p-3 rounded-xl bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)] text-left text-xs font-semibold text-[var(--text-main)] transition-all shadow-sm active:scale-[0.98]"
            >
              <span className="flex items-center gap-2.5">
                <Users className="w-4 h-4 text-purple-400 shrink-0" />
                <span>Organization & Subscription Settings</span>
              </span>
              <span className="text-[var(--text-muted)] text-xs">→</span>
            </Link>
          </div>

          {upcomingPayables.length > 0 && (
            <div className="p-4 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-3">
              <div className="flex items-center justify-between">
                <h3 className="text-xs font-bold text-[var(--text-main)] uppercase tracking-wider flex items-center gap-2">
                  <Clock className="w-3.5 h-3.5 text-amber-400" />
                  <span>{personal ? 'Bills coming due' : 'Upcoming Payables (A/P)'}</span>
                </h3>
                <span className="text-xs font-mono font-semibold text-amber-400">
                  Total: {formatMetric(kpis?.totalPayables, payablesAvailable)}
                </span>
              </div>
              <div className="space-y-2">
                {upcomingPayables.map((item) => (
                  <Link
                    key={item.purchaseInvoiceId}
                    to={`/org/${currentOrg.slug}/purchases/${item.purchaseInvoiceId}`}
                    className="p-3 rounded-xl bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)] transition flex items-center justify-between text-xs"
                  >
                    <div>
                      <div className="font-semibold text-[var(--text-main)]">{item.vendorName}</div>
                      <div className="text-[10px] text-[var(--text-muted)]">Bill #{item.invoiceNo}</div>
                    </div>
                    <div className="font-mono font-bold text-amber-400">
                      {formatMetric(item.remainingAmount, payablesAvailable)}
                    </div>
                  </Link>
                ))}
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
};

export default Dashboard;
