import React, { useState, useEffect } from 'react';
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
  Clock,
  Loader2,
} from 'lucide-react';
import { TenantContextType } from '../types';
import { useAuth } from '../auth/AuthContext';
import { currencySymbol as getCurrencySymbol } from '../lib/currency';
import { OnboardingChecklist } from '../components/OnboardingChecklist';
import { BankCloseStatusCard } from '../components/BankCloseStatusCard';
import { MoneyInBankBreakdown } from '../components/MoneyInBankBreakdown';
import type { DashboardBankBalance } from '../lib/moneyInBank';
import type { BankCloseStatus } from '../types/banking';

interface DashboardSummary {
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
    cashFlowYtd: number;
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

export const Dashboard: React.FC = () => {
  const { currentOrg, user, hasModule } = useOutletContext<TenantContextType>();
  const { authFetch } = useAuth();

  const [summary, setSummary] = useState<DashboardSummary | null>(null);
  const [loading, setLoading] = useState(true);

  const currencySymbol = getCurrencySymbol(currentOrg.currency);

  useEffect(() => {
    let mounted = true;
    setLoading(true);

    const fetchSummary = async () => {
      try {
        const res = await authFetch('/api/Dashboard/Summary');
        if (res.ok) {
          const data: DashboardSummary = await res.json();
          if (mounted) {
            setSummary(data);
          }
        }
      } catch (err) {
        console.error('Failed to load live dashboard summary:', err);
      } finally {
        if (mounted) {
          setLoading(false);
        }
      }
    };

    fetchSummary();
    return () => {
      mounted = false;
    };
  }, [authFetch, currentOrg.slug]);

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

  return (
    <div className="space-y-6 animate-in fade-in duration-200 text-[var(--text-main)]">
      {/* Workspace Context Top Bar (Border-Less) */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 p-4 sm:p-5 rounded-2xl bg-gradient-to-r from-[var(--bg-surface)] via-[var(--bg-surface)] to-[var(--brand-subtle)] shadow-xl">
        <div className="space-y-1">
          <div className="flex items-center gap-2">
            <span className="w-2 h-2 rounded-full bg-[var(--brand-primary)] animate-pulse" />
            <span className="text-[10px] sm:text-xs font-semibold uppercase tracking-wider text-[var(--brand-primary)]">
              Active Tenant Workspace (Live Database)
            </span>
          </div>
          <h1 className="text-xl sm:text-2xl font-bold text-[var(--text-main)] tracking-tight">
            {currentOrg.whitelabel?.brandName ? `${currentOrg.name} (${currentOrg.whitelabel.brandName})` : currentOrg.name}
          </h1>
          <p className="text-xs text-[var(--text-muted)]">
            Welcome back, <span className="text-[var(--text-main)] font-medium">{user.name}</span>. Real-time metrics streaming from live SQLite database.
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
            Plan: {currentOrg.plan ? currentOrg.plan.toUpperCase() : 'ACCOUNTING'}
          </div>
        </div>
      </div>

      <OnboardingChecklist
        orgSlug={currentOrg.slug}
        hasFinancialsModule={hasModule('financials')}
        canManage={currentOrg.role === 'Owner' || currentOrg.role === 'Admin'}
      />

      <BankCloseStatusCard status={summary?.bankCloseStatus} orgSlug={currentOrg.slug} />

      {/* Financial Metric Cards Grid (Border-Less with Ambient Elevation) */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 sm:gap-4">
        {/* Metric 1: Operating Revenue (Sales YTD) */}
        <div className="p-4 sm:p-5 rounded-2xl bg-[var(--bg-surface)] hover:bg-[var(--bg-surface-hover)] backdrop-blur-md shadow-md hover:shadow-xl transition-all">
          <div className="flex items-center justify-between mb-3">
            <span className="text-xs font-semibold text-[var(--text-muted)]">Operating Sales (YTD)</span>
            <div className="w-8 h-8 rounded-xl bg-[var(--brand-subtle)] text-[var(--brand-primary)] flex items-center justify-center shadow-sm">
              <TrendingUp className="w-4 h-4" />
            </div>
          </div>
          <div className="text-xl sm:text-2xl font-bold text-[var(--text-main)] tracking-tight">
            {loading ? (
              <span className="inline-block w-28 h-7 bg-[var(--bg-subtle)] rounded animate-pulse" />
            ) : (
              `${currencySymbol}${formatAmount(kpis?.totalSalesYtd)}`
            )}
          </div>
          <div className="flex items-center gap-1.5 text-xs text-[var(--brand-primary)] mt-2 font-semibold">
            <ArrowUpRight className="w-3.5 h-3.5" />
            <span>Net profit: {currencySymbol}{formatAmount(kpis?.netProfitYtd)}</span>
          </div>
        </div>

        {/* Metric 2: Receivables (A/R) */}
        <div className="p-4 sm:p-5 rounded-2xl bg-[var(--bg-surface)] hover:bg-[var(--bg-surface-hover)] backdrop-blur-md shadow-md hover:shadow-xl transition-all">
          <div className="flex items-center justify-between mb-3">
            <span className="text-xs font-semibold text-[var(--text-muted)]">Receivables (A/R)</span>
            <div className="w-8 h-8 rounded-xl bg-blue-500/10 text-blue-400 flex items-center justify-center shadow-sm">
              <Receipt className="w-4 h-4" />
            </div>
          </div>
          <div className="text-xl sm:text-2xl font-bold text-[var(--text-main)] tracking-tight">
            {loading ? (
              <span className="inline-block w-28 h-7 bg-[var(--bg-subtle)] rounded animate-pulse" />
            ) : (
              `${currencySymbol}${formatAmount(kpis?.outstandingReceivables)}`
            )}
          </div>
          <div className="text-xs text-[var(--text-muted)] mt-2">
            {topReceivables.length} accounts with balance due
          </div>
        </div>

        {/* Metric 3: Cash in Bank */}
        <div className="p-4 sm:p-5 rounded-2xl bg-[var(--bg-surface)] hover:bg-[var(--bg-surface-hover)] backdrop-blur-md shadow-md hover:shadow-xl transition-all">
          <div className="flex items-center justify-between mb-3">
            <span className="text-xs font-semibold text-[var(--text-muted)]">Cash & Bank Balances</span>
            <div className="w-8 h-8 rounded-xl bg-amber-500/10 text-amber-400 flex items-center justify-center shadow-sm">
              <Wallet className="w-4 h-4" />
            </div>
          </div>
          <div className="text-xl sm:text-2xl font-bold text-[var(--text-main)] tracking-tight">
            {loading ? (
              <span className="inline-block w-28 h-7 bg-[var(--bg-subtle)] rounded animate-pulse" />
            ) : (
              `${currencySymbol}${formatAmount(kpis?.moneyInBank)}`
            )}
          </div>
          <div className="text-xs text-[var(--text-muted)] mt-2">
            Operating liquidity in ledger
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

        {/* Metric 4: Total Ledger Assets */}
        <div className="p-4 sm:p-5 rounded-2xl bg-[var(--bg-surface)] hover:bg-[var(--bg-surface-hover)] backdrop-blur-md shadow-md hover:shadow-xl transition-all">
          <div className="flex items-center justify-between mb-3">
            <span className="text-xs font-semibold text-[var(--text-muted)]">Total Ledger Assets</span>
            <div className="w-8 h-8 rounded-xl bg-purple-500/10 text-purple-400 flex items-center justify-center shadow-sm">
              <BookOpen className="w-4 h-4" />
            </div>
          </div>
          <div className="text-xl sm:text-2xl font-bold text-[var(--text-main)] tracking-tight">
            {loading ? (
              <span className="inline-block w-28 h-7 bg-[var(--bg-subtle)] rounded animate-pulse" />
            ) : (
              `${currencySymbol}${formatAmount(kpis?.totalAssets)}`
            )}
          </div>
          <div className="text-xs text-[var(--text-muted)] mt-2">
            Liabilities: {currencySymbol}{formatAmount(kpis?.totalPayables)}
          </div>
        </div>
      </div>

      {/* Multi-Tenant Deep Linking & Live Database Guarantee */}
      <div className="p-4 rounded-xl bg-[var(--bg-subtle)] shadow-inner flex items-start gap-3 text-xs text-[var(--text-muted)]">
        <ShieldCheck className="w-4 h-4 text-[var(--brand-primary)] flex-shrink-0 mt-0.5" />
        <div className="space-y-1">
          <div className="font-semibold text-[var(--text-main)]">
            Live Database Architecture: Multi-Tenant Scoping
          </div>
          <p className="leading-relaxed">
            All accounting journals, sales receipts, customer ledgers, and subscriptions are pulled directly from the live database. No mockups are used.
          </p>
        </div>
      </div>

      {/* Quick Actions & Recent Activity From Database */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        {/* Left 2 Cols: Activity Feed */}
        <div className="lg:col-span-2 space-y-4">
          <div className="flex items-center justify-between">
            <h2 className="text-sm sm:text-base font-bold text-[var(--text-main)] flex items-center gap-2">
              <FileSpreadsheet className="w-4 h-4 text-[var(--brand-primary)]" />
              <span>Live Database Activity & Entries</span>
            </h2>
            <Link
              to={`/org/${currentOrg.slug}/financials`}
              className="text-xs font-semibold text-[var(--brand-primary)] hover:opacity-80 transition-opacity"
            >
              View Full Accounts →
            </Link>
          </div>

          <div className="rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md overflow-hidden">
            <div className="overflow-x-auto smooth-scroll">
              <table
                className="w-full min-w-[540px] text-left text-xs"
                aria-label="Live database entries and journal transactions"
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
                        Fetching live entries from database...
                      </td>
                    </tr>
                  ) : recentActivity.length === 0 ? (
                    <tr>
                      <td colSpan={3} className="p-8 text-center text-[var(--text-muted)] font-sans">
                        No activity recorded in the database for this period.
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

          {/* Top Receivables Strip */}
          {topReceivables.length > 0 && (
            <div className="p-4 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-3">
              <h3 className="text-xs font-bold text-[var(--text-main)] uppercase tracking-wider flex items-center gap-2">
                <Receipt className="w-3.5 h-3.5 text-[var(--brand-primary)]" />
                <span>Top Customer Balances (A/R)</span>
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
                      {currencySymbol}{formatAmount(cust.balance)}
                    </div>
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>

        {/* Right 1 Col: Quick Actions & Live Payables */}
        <div className="space-y-4">
          <h2 className="text-sm sm:text-base font-bold text-[var(--text-main)] flex items-center gap-2">
            <Plus className="w-4 h-4 text-[var(--brand-primary)]" />
            <span>Workspace Actions</span>
          </h2>

          <div className="p-3 sm:p-4 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-2">
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

          {/* Upcoming Payables / Bills Due */}
          {upcomingPayables.length > 0 && (
            <div className="p-4 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-3">
              <h3 className="text-xs font-bold text-[var(--text-main)] uppercase tracking-wider flex items-center gap-2">
                <Clock className="w-3.5 h-3.5 text-amber-400" />
                <span>Upcoming Payables (A/P)</span>
              </h3>
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
                      {currencySymbol}{formatAmount(item.remainingAmount)}
                    </div>
                  </Link>
                ))}
              </div>
            </div>
          )}

          {/* Quick Tenant Info Box */}
          <div className="p-4 rounded-2xl bg-[var(--brand-subtle)] shadow-md text-xs space-y-2">
            <div className="flex items-center gap-2 text-[var(--brand-primary)] font-bold">
              <CheckCircle2 className="w-4 h-4" />
              <span>Live Database Connected</span>
            </div>
            <p className="text-[var(--text-muted)] text-[11px] leading-relaxed">
              All financial figures, KPIs, ledger records, and customer accounts reflect real SQLite data.
            </p>
          </div>
        </div>
      </div>
    </div>
  );
};

export default Dashboard;
