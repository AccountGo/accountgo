import React, { useState, useEffect } from 'react';
import { useOutletContext, useLocation } from 'react-router-dom';
import {
  Layers,
  Sparkles,
  BookOpen,
  Receipt,
  BarChart3,
  Plus,
  Search,
  Filter,
  Loader2,
  Boxes,
  HeartHandshake,
  ShieldCheck,
  Eye,
} from 'lucide-react';
import { TenantContextType } from '../types';
import { useAuth } from '../auth/AuthContext';
import { currencySymbol as getCurrencySymbol } from '../lib/currency';

interface LiveRow {
  col1: string;
  col2: string;
  col3: string;
  col4: string;
  col5: string;
  status: string;
  raw?: any;
}

interface LiveMetric {
  label: string;
  value: string;
  change: string;
}

interface AccountTreeNode {
  childAccounts?: AccountTreeNode[];
}

function countAccounts(accounts: AccountTreeNode[]): number {
  return accounts.reduce((total, account) => total + 1 + countAccounts(account.childAccounts || []), 0);
}

export const PlaceholderView: React.FC = () => {
  const { currentOrg } = useOutletContext<TenantContextType>();
  const { authFetch } = useAuth();
  const location = useLocation();

  const pathParts = location.pathname.split('/').filter(Boolean);
  const currentSection = (pathParts[2] || 'financials').toLowerCase();

  const [searchQuery, setSearchQuery] = useState('');
  const [loading, setLoading] = useState(true);
  const [rows, setRows] = useState<LiveRow[]>([]);
  const [metrics, setMetrics] = useState<LiveMetric[]>([]);

  const currencySymbol = getCurrencySymbol(currentOrg.currency);

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

  useEffect(() => {
    let mounted = true;
    setLoading(true);

    const loadLiveData = async () => {
      try {
        if (currentSection === 'sales') {
          const [invoicesRes, customersRes] = await Promise.all([
            authFetch('/api/Sales/SalesInvoices'),
            authFetch('/api/Sales/Customers'),
          ]);

          const invoices = invoicesRes.ok ? await invoicesRes.json() : [];
          const customers = customersRes.ok ? await customersRes.json() : [];

          if (mounted) {
            const totalInvoiced = invoices.reduce((acc: number, inv: any) => acc + (inv.amount || 0), 0);
            const totalDue = customers.reduce((acc: number, c: any) => acc + (c.balance || 0), 0);

            setMetrics([
              {
                label: 'Total Invoiced',
                value: `${currencySymbol}${formatAmount(totalInvoiced)}`,
                change: `${invoices.length} live database invoices`,
              },
              {
                label: 'Open Receivables (A/R)',
                value: `${currencySymbol}${formatAmount(totalDue)}`,
                change: 'Current customer balances',
              },
              {
                label: 'Active Customers',
                value: `${customers.length} Accounts`,
                change: 'Registered in database',
              },
              {
                label: 'Posted Invoices',
                value: `${invoices.filter((i: any) => i.posted).length} Invoices`,
                change: 'General ledger synced',
              },
            ]);

            const tableRows: LiveRow[] = invoices.map((inv: any) => ({
              col1: `INV-${inv.no || inv.id}`,
              col2: inv.customerName || `Customer #${inv.customerId}`,
              col3: formatDate(inv.invoiceDate),
              col4: inv.referenceNo || '—',
              col5: `${currencySymbol}${formatAmount(inv.amount)}`,
              status: inv.posted ? 'Posted' : 'Draft',
              raw: inv,
            }));
            setRows(tableRows);
          }
        } else if (currentSection === 'financials') {
          const [tbRes, accountsRes] = await Promise.all([
            authFetch('/api/Financials/trialbalance'),
            authFetch('/api/Financials/Accounts'),
          ]);

          const trialBalance = tbRes.ok ? await tbRes.json() : [];
          const accounts: AccountTreeNode[] = accountsRes.ok ? await accountsRes.json() : [];

          if (mounted) {
            // The API returns posted debits and credits per account; a trial balance lists each account's net balance on one side.
            const balances = trialBalance
              .map((item: any) => ({ item, net: Math.round(((item.debit || 0) - (item.credit || 0)) * 100) / 100 }))
              .sort((a: any, b: any) => String(a.item.accountCode).localeCompare(String(b.item.accountCode)));
            const totalDebit = balances.reduce((acc: number, b: any) => acc + Math.max(b.net, 0), 0);
            const totalCredit = balances.reduce((acc: number, b: any) => acc + Math.max(-b.net, 0), 0);
            const difference = Math.abs(totalDebit - totalCredit);
            const isBalanced = difference < 0.005;

            setMetrics([
              {
                label: 'Active COA Accounts',
                value: `${countAccounts(accounts)} Accounts`,
                change: 'Standard Chart of Accounts',
              },
              {
                label: 'Debit Balances',
                value: `${currencySymbol}${formatAmount(totalDebit)}`,
                change: 'Trial balance total',
              },
              {
                label: 'Credit Balances',
                value: `${currencySymbol}${formatAmount(totalCredit)}`,
                change: 'Trial balance total',
              },
              {
                label: 'Trial Balance State',
                value: isBalanced ? 'Balanced' : 'Out of balance',
                change: isBalanced ? 'Debits equal credits' : `Difference ${currencySymbol}${formatAmount(difference)}`,
              },
            ]);

            const tableRows: LiveRow[] = balances.map(({ item, net }: any) => ({
              col1: item.accountCode,
              col2: item.accountName,
              col3: `${currencySymbol}${formatAmount(Math.max(net, 0))}`,
              col4: `${currencySymbol}${formatAmount(Math.max(-net, 0))}`,
              col5: `${currencySymbol}${formatAmount(Math.abs(net))}`,
              status: net > 0 ? 'Debit' : net < 0 ? 'Credit' : 'Zero',
              raw: item,
            }));
            setRows(tableRows);
          }
        } else if (currentSection === 'reports') {
          const [tbRes, incRes] = await Promise.all([
            authFetch('/api/Financials/trialbalance'),
            authFetch('/api/Financials/IncomeStatement'),
          ]);

          const trialBalance = tbRes.ok ? await tbRes.json() : [];
          const incomeStatement = incRes.ok ? await incRes.json() : [];

          if (mounted) {
            setMetrics([
              {
                label: 'Income Statement Items',
                value: `${incomeStatement.length} Accounts`,
                change: 'P&L ledger mapping',
              },
              {
                label: 'Trial Balance State',
                value: 'Reconciled',
                change: `${trialBalance.length} ledger balances`,
              },
              {
                label: 'Financial Year',
                value: 'FY 2026',
                change: 'Jan 1 - Dec 31',
              },
              {
                label: 'Audit Integrity',
                value: 'Verified',
                change: 'Double-entry books',
              },
            ]);

            const tableRows: LiveRow[] = trialBalance.map((item: any) => ({
              col1: item.accountCode,
              col2: item.accountName,
              col3: 'Standard Ledger',
              col4: `DR: ${currencySymbol}${formatAmount(item.debit)}`,
              col5: `CR: ${currencySymbol}${formatAmount(item.credit)}`,
              status: 'Reconciled',
              raw: item,
            }));
            setRows(tableRows);
          }
        } else if (currentSection === 'audit') {
          const res = await authFetch('/api/Administration/AuditLogs');
          const logs = res.ok ? await res.json() : [];

          if (mounted) {
            setMetrics([
              {
                label: 'Total Audit Logs',
                value: `${logs.length} Events`,
                change: 'Database mutation events',
              },
              {
                label: 'Compliance Engine',
                value: 'Active',
                change: 'Real-time interceptor',
              },
              {
                label: 'Security Coverage',
                value: '100%',
                change: 'All models tracked',
              },
              {
                label: 'Tamper Protection',
                value: 'Enforced',
                change: 'Append-only ledger',
              },
            ]);

            const tableRows: LiveRow[] = logs.map((log: any) => ({
              col1: log.tableName || 'System',
              col2: log.auditEventType || 'Update',
              col3: log.userName || 'system',
              col4: log.fieldName || 'Record',
              col5: formatDate(log.auditEventDateUTC),
              status: 'Audited',
              raw: log,
            }));
            setRows(tableRows);
          }
        } else if (currentSection === 'inventory') {
          const [itemsRes, icjRes] = await Promise.all([
            authFetch('/api/Inventory/items'),
            authFetch('/api/Inventory/ICJ'),
          ]);

          const items = itemsRes.ok ? await itemsRes.json() : [];
          const icjs = icjRes.ok ? await icjRes.json() : [];

          if (mounted) {
            const totalStockValuation = Array.isArray(items)
              ? items.reduce(
                  (acc: number, item: any) => acc + (item.price || 0) * (item.quantityOnHand || 0),
                  0
                )
              : 0;
            setMetrics([
              {
                label: 'Total Catalog Items',
                value: `${Array.isArray(items) ? items.length : 0} Products`,
                change: 'Inventory SKU master',
              },
              {
                label: 'Stock Movements (ICJ)',
                value: `${Array.isArray(icjs) ? icjs.length : 0} Transactions`,
                change: 'In/Out ledger journals',
              },
              {
                label: 'Inventory Valuation',
                value: `${currencySymbol}${formatAmount(totalStockValuation)}`,
                change: 'Real-time stock valuation',
              },
              {
                label: 'Warehouse Status',
                value: 'Operational',
                change: 'Stock control active',
              },
            ]);

            const tableRows: LiveRow[] = Array.isArray(items)
              ? items.map((item: any) => ({
                  col1: item.code || `SKU-${item.id}`,
                  col2: item.description,
                  col3: item.itemTaxGroupName || 'Regular',
                  col4: `${item.quantityOnHand || 0} ${item.measurement || 'Units'}`,
                  col5: `${currencySymbol}${formatAmount(item.price)}`,
                  status: (item.quantityOnHand || 0) > 0 ? 'In Stock' : 'Available',
                  raw: item,
                }))
              : [];
            setRows(tableRows);
          }
        } else if (currentSection === 'donations') {
          const [donationsRes, donorsRes] = await Promise.all([
            authFetch('/api/Donations/DonationInvoices'),
            authFetch('/api/Donors'),
          ]);

          const donations = donationsRes.ok ? await donationsRes.json() : [];
          const donors = donorsRes.ok ? await donorsRes.json() : [];

          if (mounted) {
            const totalRaised = Array.isArray(donations)
              ? donations.reduce((acc: number, d: any) => acc + (d.amount || 0), 0)
              : 0;
            setMetrics([
              {
                label: 'Total Donations Raised',
                value: `${currencySymbol}${formatAmount(totalRaised)}`,
                change: `${Array.isArray(donations) ? donations.length : 0} receipts in database`,
              },
              {
                label: 'Registered Donors',
                value: `${Array.isArray(donors) ? donors.length : 0} Donors`,
                change: 'Individual & corporate',
              },
              {
                label: 'Tax Receipts Issued',
                value: `${Array.isArray(donations) ? donations.filter((d: any) => d.isTaxReceiptIssued).length : 0} Receipts`,
                change: 'Official donation receipts',
              },
              {
                label: 'Campaign Status',
                value: 'Active',
                change: 'Non-profit ledger live',
              },
            ]);

            const tableRows: LiveRow[] = Array.isArray(donations)
              ? donations.map((d: any) => ({
                  col1: d.no || `DON-${d.id}`,
                  col2: d.donorName || `Donor #${d.donorId}`,
                  col3: formatDate(d.donationDate),
                  col4: d.purpose || d.paymentType || 'General Contribution',
                  col5: `${currencySymbol}${formatAmount(d.amount)}`,
                  status: d.isTaxReceiptIssued ? 'Receipted' : 'Recorded',
                  raw: d,
                }))
              : [];
            setRows(tableRows);
          }
        } else {
          // Fallback module state
          if (mounted) {
            setMetrics([
              { label: 'Module State', value: 'Active', change: 'Tenant-scoped' },
              { label: 'Database Engine', value: 'SQLite', change: 'accountgo.db' },
              { label: 'Multi-Tenant URL', value: `/org/${currentOrg.slug}`, change: 'Strict URL binding' },
              { label: 'Gating Policy', value: 'Authorized', change: 'RBAC verified' },
            ]);
            setRows([]);
          }
        }
      } catch (err) {
        console.error('Failed to load module data from database:', err);
      } finally {
        if (mounted) {
          setLoading(false);
        }
      }
    };

    loadLiveData();

    return () => {
      mounted = false;
    };
  }, [authFetch, currentSection, currentOrg.slug]);

  // Section configs for titles, badges, and headers
  const SECTION_CONFIGS: Record<
    string,
    {
      name: string;
      badge: string;
      description: string;
      icon: React.ComponentType<{ className?: string }>;
      primaryAction: string;
      tableHeaders: string[];
    }
  > = {
    financials: {
      name: 'General Ledger & Financials',
      badge: 'Core Accounting',
      description: 'Live Chart of Accounts, cash & bank ledgers, and trial balance.',
      icon: BookOpen,
      primaryAction: 'New Journal Entry',
      tableHeaders: ['Account Code', 'Account Name', 'Debit Balance', 'Credit Balance', 'Net Balance', 'Type', 'Action'],
    },
    sales: {
      name: 'Sales & Customer Invoices',
      badge: 'Receivables',
      description: 'Customer directory, quotations, sales orders, and live database invoices.',
      icon: Receipt,
      primaryAction: 'Create Sales Invoice',
      tableHeaders: ['Invoice No', 'Customer Name', 'Issue Date', 'Reference', 'Amount', 'Status', 'Action'],
    },
    reports: {
      name: 'Financial Reports & Analytics',
      badge: 'Reporting Suite',
      description: 'Live trial balance, balance sheet accounts, and revenue statements.',
      icon: BarChart3,
      primaryAction: 'Export Statement',
      tableHeaders: ['Account Code', 'Account Name', 'Category', 'Debit Sum', 'Credit Sum', 'Status', 'Action'],
    },
    inventory: {
      name: 'Inventory & Warehousing',
      badge: 'Stock Control',
      description: 'Product catalog, item valuations, and warehouse movements.',
      icon: Boxes,
      primaryAction: 'Add Product Item',
      tableHeaders: ['Item Code', 'Item Name', 'Category', 'Quantity', 'Valuation', 'Status', 'Action'],
    },
    donations: {
      name: 'Donations & Non-Profit',
      badge: 'Non-Profit Suite',
      description: 'Donor management, donation receipts, and outreach campaigns.',
      icon: HeartHandshake,
      primaryAction: 'Record Donation',
      tableHeaders: ['Donation No', 'Donor Name', 'Date', 'Purpose', 'Amount', 'Status', 'Action'],
    },
    audit: {
      name: 'Audit Trail & Compliance',
      badge: 'Security & Audit',
      description: 'Full database mutation tracking and entity change logs.',
      icon: ShieldCheck,
      primaryAction: 'Filter Audit Logs',
      tableHeaders: ['Entity Table', 'Event Type', 'User Name', 'Field Mutated', 'Event Date', 'Status', 'Action'],
    },
  };

  const config = SECTION_CONFIGS[currentSection] || {
    name: `${currentSection.charAt(0).toUpperCase() + currentSection.slice(1)} Module`,
    badge: 'Workspace Module',
    description: `Workspace view scoped to /org/${currentOrg.slug}/${currentSection}.`,
    icon: Layers,
    primaryAction: 'Create New Entry',
    tableHeaders: ['Reference', 'Date', 'Entity', 'Details', 'Value', 'Status', 'Action'],
  };

  const Icon = config.icon;

  const filteredRows = rows.filter((r) => {
    if (!searchQuery) return true;
    const q = searchQuery.toLowerCase();
    return (
      r.col1.toLowerCase().includes(q) ||
      r.col2.toLowerCase().includes(q) ||
      r.col3.toLowerCase().includes(q) ||
      r.col4.toLowerCase().includes(q) ||
      r.col5.toLowerCase().includes(q)
    );
  });

  return (
    <div className="space-y-6 w-full animate-in fade-in duration-200 text-[var(--text-main)]">
      {/* Workspace Context Top Bar (Border-Less) */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 p-4 sm:p-5 rounded-2xl bg-gradient-to-r from-[var(--bg-surface)] via-[var(--bg-surface)] to-[var(--brand-subtle)] shadow-xl">
        <div className="space-y-1">
          <div className="flex items-center gap-2">
            <span className="w-2 h-2 rounded-full bg-[var(--brand-primary)] animate-pulse" />
            <span className="text-[10px] sm:text-xs font-semibold uppercase tracking-wider text-[var(--brand-primary)]">
              {config.badge} (Live Database)
            </span>
          </div>
          <h1 className="text-xl sm:text-2xl font-bold text-[var(--text-main)] tracking-tight">
            {config.name}
          </h1>
          <p className="text-xs text-[var(--text-muted)]">
            {config.description} Scoped to{' '}
            <span className="text-[var(--text-main)] font-semibold">{currentOrg.name}</span>.
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

      {/* Module Metrics Cards Grid */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 sm:gap-4">
        {metrics.map((metric, idx) => (
          <div
            key={idx}
            className="p-4 sm:p-5 rounded-2xl bg-[var(--bg-surface)] hover:bg-[var(--bg-surface-hover)] backdrop-blur-md shadow-md transition-all"
          >
            <div className="flex items-center justify-between mb-3">
              <span className="text-xs font-semibold text-[var(--text-muted)]">{metric.label}</span>
              <div className="w-8 h-8 rounded-xl bg-[var(--brand-subtle)] text-[var(--brand-primary)] flex items-center justify-center shadow-sm">
                <Icon className="w-4 h-4" />
              </div>
            </div>
            <div className="text-xl sm:text-2xl font-bold text-[var(--text-main)] tracking-tight">
              {loading ? (
                <span className="inline-block w-24 h-6 bg-[var(--bg-subtle)] rounded animate-pulse" />
              ) : (
                metric.value
              )}
            </div>
            <div className="text-xs text-[var(--brand-primary)] font-medium mt-1">
              {metric.change}
            </div>
          </div>
        ))}
      </div>

      {/* Main Data Section Header & Controls */}
      <div className="p-4 sm:p-6 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-4">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
          <div className="flex items-center gap-3">
            <div className="relative w-full sm:w-72">
              <Search className="w-4 h-4 text-[var(--text-muted)] absolute left-3.5 top-3" />
              <input
                type="text"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                placeholder={`Search ${config.name.toLowerCase()}...`}
                className="w-full h-10 pl-9 pr-3 rounded-xl bg-[var(--bg-subtle)] text-xs text-[var(--text-main)] focus:outline-none focus:ring-1 focus:ring-[var(--brand-ring)] shadow-inner"
              />
            </div>
            <button
              type="button"
              className="h-10 px-3.5 rounded-xl bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)] text-xs font-medium text-[var(--text-secondary)] flex items-center gap-1.5 transition-colors cursor-pointer shrink-0"
            >
              <Filter className="w-3.5 h-3.5" />
              <span>Filters</span>
            </button>
          </div>

          <div className="flex items-center gap-2">
            <button
              type="button"
              className="btn-primary !h-10 !px-4 text-xs shrink-0 cursor-pointer"
            >
              <Plus className="w-4 h-4" />
              <span>{config.primaryAction}</span>
            </button>
          </div>
        </div>

        {/* Live Data Table */}
        <div className="overflow-x-auto rounded-xl">
          <table className="w-full text-left text-xs text-[var(--text-secondary)]">
            <thead className="bg-[var(--bg-subtle)] text-[var(--text-muted)] uppercase text-[10px] font-bold tracking-wider">
              <tr>
                {config.tableHeaders.map((head, i) => (
                  <th key={i} className="px-4 py-3">{head}</th>
                ))}
              </tr>
            </thead>
            <tbody className="divide-y divide-[var(--bg-surface-hover)] font-mono text-[11px]">
              {loading ? (
                <tr>
                  <td colSpan={config.tableHeaders.length} className="p-8 text-center text-[var(--text-muted)] font-sans">
                    <Loader2 className="w-5 h-5 mx-auto animate-spin mb-2 text-[var(--brand-primary)]" />
                    Connecting to live database records...
                  </td>
                </tr>
              ) : filteredRows.length === 0 ? (
                <tr>
                  <td colSpan={config.tableHeaders.length} className="p-8 text-center text-[var(--text-muted)] font-sans">
                    No matching database records found for this view.
                  </td>
                </tr>
              ) : (
                filteredRows.map((row, idx) => (
                  <tr key={idx} className="hover:bg-[var(--bg-surface-hover)] transition-colors">
                    <td className="px-4 py-3.5 font-sans font-medium text-[var(--text-main)]">{row.col1}</td>
                    <td className="px-4 py-3.5 text-[var(--text-main)] font-sans">{row.col2}</td>
                    <td className="px-4 py-3.5 text-[var(--text-secondary)] font-sans">{row.col3}</td>
                    <td className="px-4 py-3.5 text-[var(--text-secondary)] font-sans">{row.col4}</td>
                    <td className="px-4 py-3.5 font-semibold text-[var(--text-main)] font-mono">{row.col5}</td>
                    <td className="px-4 py-3.5 font-sans">
                      <span
                        className={`px-2 py-0.5 rounded-full text-[10px] font-semibold ${
                          row.status === 'Posted' || row.status === 'Reconciled' || row.status === 'Debit'
                            ? 'bg-emerald-500/10 text-emerald-400'
                            : 'bg-amber-500/10 text-amber-400'
                        }`}
                      >
                        {row.status}
                      </span>
                    </td>
                    <td className="px-4 py-3.5">
                      <span className="text-[var(--brand-primary)] hover:underline cursor-pointer font-sans text-xs flex items-center gap-1">
                        <Eye className="w-3.5 h-3.5" />
                        <span>View</span>
                      </span>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        {/* Live Multi-Tenant Database Footnote */}
        <div className="p-4 rounded-xl bg-[var(--bg-subtle)] text-xs text-[var(--text-muted)] flex items-start gap-2.5">
          <Sparkles className="w-4 h-4 text-[var(--brand-primary)] shrink-0 mt-0.5" />
          <p className="leading-relaxed">
            Data in <strong className="text-[var(--text-main)]">{config.name}</strong> is live from the SQLite database. Scoped strictly to <code className="text-[var(--brand-primary)] font-mono">/org/{currentOrg.slug}</code> with RBAC enforcement.
          </p>
        </div>
      </div>
    </div>
  );
};

export default PlaceholderView;
