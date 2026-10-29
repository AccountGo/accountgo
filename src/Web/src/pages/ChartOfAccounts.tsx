import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link, useOutletContext } from 'react-router-dom';
import {
  AlertCircle,
  AlertTriangle,
  Archive,
  ArchiveRestore,
  ArrowLeft,
  CheckCircle2,
  ChevronDown,
  ChevronRight,
  Download,
  Edit2,
  FileDown,
  FileText,
  Loader2,
  Plus,
  RefreshCw,
  Search,
  Shield,
  Sparkles,
  Trash2,
  Upload,
  X
} from 'lucide-react';
import { TenantContextType } from '../types';
import { useAuth } from '../auth/AuthContext';
import { PermissionDeniedCard, useTenantContext } from '../components/Gating';
import { currencySymbol as getCurrencySymbol } from '../lib/currency';
import { PageHeader, HeaderButton } from '../components/PageHeader';
import { ModalShell } from '../components/ModalShell';

export interface ChartOfAccountsStatus {
  isEmptyBooks: boolean;
  postedTransactionsCount: number;
  unpostedDraftCount: number;
  canReplace: boolean;
}

export interface ControlRoleMapping {
  roleKey: string;
  roleTitle: string;
  category: string;
  currentAccountId: number;
  currentAccountCode: string;
  currentAccountName: string;
  targetAccountCode?: string | null;
}

export interface CoaImportRowPreview {
  rowNumber: number;
  accountCode: string;
  accountName: string;
  parentAccountCode?: string | null;
  accountClassId: number;
  accountClassName?: string | null;
  isContraAccount: boolean;
  isCash: boolean;
  description?: string | null;
  status?: string | null;
  action: 'create' | 'update' | 'skip' | 'error';
  errors: string[];
  warnings: string[];
}

export interface CoaImportPreview {
  mode: 'append' | 'upsert' | 'replace';
  totalRows: number;
  createCount: number;
  updateCount: number;
  skipCount: number;
  retireCount?: number;
  deleteCount?: number;
  errorCount: number;
  warningCount: number;
  fileErrors: string[];
  fileWarnings: string[];
  rows: CoaImportRowPreview[];
  hasErrors: boolean;
  canCommit: boolean;
  isEmptyBooks?: boolean;
  canReplace?: boolean;
  controlMappings?: ControlRoleMapping[];
}

export interface CoaImportCommitResult {
  mode: string;
  totalRowsProcessed: number;
  createdCount: number;
  updatedCount: number;
  skippedCount: number;
  retiredCount?: number;
  deletedCount?: number;
}

export interface ApiAccount {
  id: number;
  accountClassId: number;
  accountCode: string;
  accountName: string;
  accountClassName?: string;
  normalBalance?: 'Dr' | 'Cr' | 'NA';
  isCash: boolean;
  isContraAccount: boolean;
  canPost: boolean;
  archived?: boolean;
  archivedAt?: string | null;
  isProtected?: boolean;
  protectionReason?: string | null;
  canDelete?: boolean;
  deleteBlockedReason?: string | null;
  parentAccountId?: number | null;
  description?: string | null;
  // Signed on the account class's normal side, so contra accounts come back negative.
  balance: number;
  totalBalance: number;
  childAccounts: ApiAccount[];
}

interface Row {
  account: ApiAccount;
  level: number;
  rollup: number;
  hasChildren: boolean;
  parentId: number | null;
}

// Statement order: balance sheet first, then income statement, then closing accounts.
const CLASS_LABELS: Record<number, string> = {
  1: 'Assets',
  2: 'Liabilities',
  3: 'Equity',
  4: 'Revenue',
  5: 'Expenses',
  6: 'Closing accounts',
};

const rollupOf = (a: ApiAccount): number => (a.balance || 0) + (a.totalBalance || 0);

// A contra account's natural balance is the opposite of its class's sign.
const isAbnormal = (a: ApiAccount, rollup: number) => (a.isContraAccount ? -rollup : rollup) < 0;

const sortByCode = (list: ApiAccount[]) =>
  [...list].sort((x, y) => x.accountCode.localeCompare(y.accountCode, undefined, { numeric: true }));

function flatten(accounts: ApiAccount[], level = 1, parentId: number | null = null, out: Row[] = []): Row[] {
  for (const account of sortByCode(accounts)) {
    const children = account.childAccounts || [];
    out.push({ account, level, rollup: rollupOf(account), hasChildren: children.length > 0, parentId });
    flatten(children, level + 1, account.id, out);
  }
  return out;
}

export const ChartOfAccounts: React.FC<{ initialChartStatus?: ChartOfAccountsStatus | null }> = ({
  initialChartStatus = null,
}) => {
  const { currentOrg } = useOutletContext<TenantContextType>();
  const { hasPermission } = useTenantContext();
  const { authFetch } = useAuth();
  const symbol = getCurrencySymbol(currentOrg.currency);

  const canManage = hasPermission('financials.accounts.manage');

  const [accounts, setAccounts] = useState<ApiAccount[]>([]);
  /** GL account id → bank account id for linked cash accounts. */
  const [bankByAccountId, setBankByAccountId] = useState<Map<number, number>>(new Map());
  const [loading, setLoading] = useState(true);
  const [loadFailed, setLoadFailed] = useState(false);
  const [query, setQuery] = useState('');
  const [classFilter, setClassFilter] = useState<number | null>(null);
  const [hideZero, setHideZero] = useState(false);
  const [hideArchived, setHideArchived] = useState(false);
  const [collapsed, setCollapsed] = useState<Set<number>>(new Set());
  const [exportMenuOpen, setExportMenuOpen] = useState(false);
  const exportDropdownRef = useRef<HTMLDivElement>(null);
  const tableRef = useRef<HTMLTableSectionElement>(null);

  // Dialog state
  const [editorOpen, setEditorOpen] = useState(false);
  const [editingAccount, setEditingAccount] = useState<ApiAccount | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  // Form fields
  const [formData, setFormData] = useState({
    accountCode: '',
    accountName: '',
    accountClassId: 1,
    parentAccountId: '' as number | '',
    description: '',
    isCash: false,
    isContraAccount: false,
  });

  // Action feedback dialog
  const [actionBlocked, setActionBlocked] = useState<{ title: string; message: string } | null>(null);
  const [deleteConfirm, setDeleteConfirm] = useState<ApiAccount | null>(null);
  const [actionBusy, setActionBusy] = useState(false);

  // Import Wizard State
  const [chartStatus, setChartStatus] = useState<ChartOfAccountsStatus | null>(initialChartStatus);
  const [importModalOpen, setImportModalOpen] = useState(false);
  const [importStep, setImportStep] = useState<'upload' | 'preview' | 'success'>('upload');
  const [importMode, setImportMode] = useState<'append' | 'upsert' | 'replace'>('append');
  const [retireMissing, setRetireMissing] = useState(false);
  const [controlMappings, setControlMappings] = useState<Record<string, string>>({});
  const [importFile, setImportFile] = useState<File | null>(null);
  const [importCsvText, setImportCsvText] = useState<string>('');
  const [importClientWarning, setImportClientWarning] = useState<string | null>(null);
  const [validatingImport, setValidatingImport] = useState(false);
  const [committingImport, setCommittingImport] = useState(false);
  const [importPreview, setImportPreview] = useState<CoaImportPreview | null>(null);
  const [importResult, setImportResult] = useState<CoaImportCommitResult | null>(null);
  const [importFilterAction, setImportFilterAction] = useState<string>('all');
  const [importGeneralError, setImportGeneralError] = useState<string | null>(null);
  const importFileInputRef = useRef<HTMLInputElement>(null);

  const isOwner = currentOrg.role?.toLowerCase() === 'owner';

  const load = useCallback(async () => {
    setLoading(true);
    setLoadFailed(false);
    try {
      const [res, statusRes, banksRes] = await Promise.all([
        authFetch('/api/Financials/Accounts'),
        authFetch('/api/Financials/ChartStatus').catch(() => null),
        authFetch('/api/Banking/Accounts?includeArchived=false').catch(() => null),
      ]);
      if (!res.ok) throw new Error('Failed to load accounts');
      const data = await res.json() as ApiAccount[];
      setAccounts(Array.isArray(data) ? data : []);

      if (statusRes && statusRes.ok) {
        const s = await statusRes.json() as ChartOfAccountsStatus;
        setChartStatus(s);
      }

      const linked = new Map<number, number>();
      if (banksRes && banksRes.ok) {
        const list = await banksRes.json() as { accounts?: Array<{ id: number; accountId?: number | null }> };
        for (const bank of list.accounts ?? []) {
          if (bank.accountId != null) linked.set(bank.accountId, bank.id);
        }
      }
      setBankByAccountId(linked);
    } catch {
      setLoadFailed(true);
    } finally {
      setLoading(false);
    }
  }, [authFetch]);

  useEffect(() => {
    load();
  }, [load, currentOrg.slug]);

  // Close export dropdown when clicking outside
  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (exportDropdownRef.current && !exportDropdownRef.current.contains(e.target as Node)) {
        setExportMenuOpen(false);
      }
    };
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  const allRows = useMemo(() => flatten(accounts), [accounts]);

  // Search and filters keep the ancestors of every match so the hierarchy stays readable.
  const visibleIds = useMemo(() => {
    const q = query.trim().toLowerCase();
    const byId = new Map(allRows.map((r) => [r.account.id, r]));
    const keep = new Set<number>();
    for (const row of allRows) {
      const a = row.account;
      if (classFilter !== null && a.accountClassId !== classFilter) continue;
      if (hideArchived && a.archived) continue;
      if (q && !a.accountCode.toLowerCase().includes(q) && !a.accountName.toLowerCase().includes(q)) continue;
      if (hideZero && (row.hasChildren || row.rollup === 0)) continue;
      for (let r: Row | undefined = row; r && !keep.has(r.account.id); r = r.parentId === null ? undefined : byId.get(r.parentId)) {
        keep.add(r.account.id);
      }
    }
    return keep;
  }, [allRows, query, classFilter, hideZero, hideArchived]);

  const filtering = query.trim() !== '' || hideZero || hideArchived;

  const rows = useMemo(() => {
    const hiddenUnder = new Set<number>();
    return allRows.filter((row) => {
      if (row.parentId !== null && hiddenUnder.has(row.parentId)) {
        hiddenUnder.add(row.account.id);
        return false;
      }
      if (!visibleIds.has(row.account.id)) return false;
      if (!filtering && collapsed.has(row.account.id)) hiddenUnder.add(row.account.id);
      return true;
    });
  }, [allRows, visibleIds, collapsed, filtering]);

  const classes = useMemo(
    () => Array.from(new Set(accounts.map((a) => a.accountClassId))).sort((a, b) => a - b),
    [accounts]
  );

  const toggle = (id: number, open?: boolean) =>
    setCollapsed((prev) => {
      const next = new Set(prev);
      const isOpen = !next.has(id);
      if (open ?? !isOpen) next.delete(id);
      else next.add(id);
      return next;
    });

  const formatAmount = (value: number) => {
    const text = `${symbol}${Math.abs(value).toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
    return value < 0 ? `(${text})` : text;
  };

  // Export functions
  const triggerDownload = (content: string, filename: string) => {
    const blob = new Blob([content], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = filename;
    link.click();
    URL.revokeObjectURL(url);
    setExportMenuOpen(false);
  };

  const exportStructure = async () => {
    try {
      const res = await authFetch('/api/Financials/ExportStructure');
      if (res.ok) {
        const text = await res.text();
        triggerDownload(text, `${currentOrg.slug}-chart-of-accounts-structure.csv`);
        return;
      }
    } catch {
      // Fallback to client generation
    }

    const escape = (v: string) => (/[",\n\r]/.test(v) ? `"${v.replace(/"/g, '""')}"` : v);
    const lines = [
      '# AccountGo-COA-v1',
      ['AccountCode', 'AccountName', 'ParentAccountCode', 'AccountClass', 'Contra', 'Cash', 'Description', 'Status'].join(','),
    ];
    for (const { account: a } of allRows) {
      const parent = a.parentAccountId ? allRows.find((r) => r.account.id === a.parentAccountId)?.account.accountCode || '' : '';
      lines.push([
        a.accountCode,
        a.accountName,
        parent,
        CLASS_LABELS[a.accountClassId] || a.accountClassName || 'Assets',
        a.isContraAccount ? 'TRUE' : 'FALSE',
        a.isCash ? 'TRUE' : 'FALSE',
        a.description || '',
        a.archived ? 'Archived' : 'Active',
      ].map(escape).join(','));
    }
    triggerDownload(lines.join('\n'), `${currentOrg.slug}-chart-of-accounts-structure.csv`);
  };

  const exportSnapshot = async () => {
    try {
      const res = await authFetch('/api/Financials/ExportSnapshot');
      if (res.ok) {
        const text = await res.text();
        triggerDownload(text, `${currentOrg.slug}-chart-of-accounts-snapshot.csv`);
        return;
      }
    } catch {
      // Fallback to client generation
    }

    const escape = (v: string) => (/[",\n\r]/.test(v) ? `"${v.replace(/"/g, '""')}"` : v);
    const lines = [
      '# AccountGo-COA-Snapshot-v1',
      ['AccountCode', 'AccountName', 'ParentAccountCode', 'AccountClass', 'Contra', 'Cash', 'Description', 'Status', 'Level', 'NormalBalance', 'Header', 'Balance'].join(','),
    ];
    for (const { account: a, level, rollup, hasChildren } of allRows) {
      const parent = a.parentAccountId ? allRows.find((r) => r.account.id === a.parentAccountId)?.account.accountCode || '' : '';
      lines.push([
        a.accountCode,
        a.accountName,
        parent,
        CLASS_LABELS[a.accountClassId] || a.accountClassName || 'Assets',
        a.isContraAccount ? 'TRUE' : 'FALSE',
        a.isCash ? 'TRUE' : 'FALSE',
        a.description || '',
        a.archived ? 'Archived' : 'Active',
        String(level),
        a.normalBalance || '',
        hasChildren ? 'yes' : 'no',
        rollup.toFixed(2),
      ].map(escape).join(','));
    }
    triggerDownload(lines.join('\n'), `${currentOrg.slug}-chart-of-accounts-snapshot.csv`);
  };

  const downloadSampleTemplate = async () => {
    try {
      const res = await authFetch('/api/Financials/ExportTemplate');
      if (res.ok) {
        const text = await res.text();
        triggerDownload(text, 'accountgo-coa-structure-template.csv');
        return;
      }
    } catch {
      // Fallback
    }

    const template = [
      '# AccountGo-COA-v1',
      'AccountCode,AccountName,ParentAccountCode,AccountClass,Contra,Cash,Description,Status',
      '10000,Assets,,Assets,FALSE,FALSE,Asset summary account,Active',
      '10100,Current Assets,10000,Assets,FALSE,FALSE,Current assets group,Active',
      '10110,Cash & Bank,10100,Assets,FALSE,TRUE,Cash & bank holdings,Active',
      '10111,Operating Bank Account,10110,Assets,FALSE,TRUE,Main checking account,Active',
      '10120,Accounts Receivable,10100,Assets,FALSE,FALSE,Trade debtors,Active',
      '10121,Allowance for Doubtful Accounts,10100,Assets,TRUE,FALSE,Contra asset,Active',
      '20000,Liabilities,,Liabilities,FALSE,FALSE,Liabilities summary,Active',
      '20100,Current Liabilities,20000,Liabilities,FALSE,FALSE,Current liabilities,Active',
      '20110,Accounts Payable,20100,Liabilities,FALSE,FALSE,Trade creditors,Active',
      '30000,Equity,,Equity,FALSE,FALSE,Equity summary,Active',
      '30100,Owner\'s Equity,30000,Equity,FALSE,FALSE,Contributed capital,Active',
      '40000,Revenue,,Revenue,FALSE,FALSE,Operating revenue,Active',
      '40100,Sales Revenue,40000,Revenue,FALSE,FALSE,Product and service sales,Active',
      '50000,Expenses,,Expenses,FALSE,FALSE,Operating expenses,Active',
      '50100,Cost of Goods Sold,50000,Expenses,FALSE,FALSE,Direct costs,Active',
      '50200,General & Administrative Expenses,50000,Expenses,FALSE,FALSE,Overhead,Active',
    ].join('\n');
    triggerDownload(template, 'accountgo-coa-structure-template.csv');
  };

  const openImportWizard = (initialMode: 'append' | 'upsert' | 'replace' = 'append') => {
    setImportStep('upload');
    setImportMode(initialMode);
    setRetireMissing(false);
    setControlMappings({});
    setImportFile(null);
    setImportCsvText('');
    setImportClientWarning(null);
    setImportGeneralError(null);
    setImportPreview(null);
    setImportResult(null);
    setImportFilterAction('all');
    setImportModalOpen(true);
  };

  const handleFileSelect = (file: File) => {
    setImportFile(file);
    setImportClientWarning(null);
    setImportGeneralError(null);
    const reader = new FileReader();
    reader.onload = (e) => {
      const text = (e.target?.result as string) || '';
      setImportCsvText(text);
      if (text) {
        const lines = text.split(/\r?\n/).filter((l) => l.trim().length > 0);
        const firstLine = lines[0] || '';
        const secondLine = lines[1] || '';
        if (
          firstLine.includes('AccountGo-COA-Snapshot-v1') ||
          /balance/i.test(firstLine) ||
          /balance/i.test(secondLine)
        ) {
          setImportClientWarning(
            'Chart of accounts import only supports structure. Files containing balance column or snapshot format cannot be imported. Opening balances must be loaded via conversion journal (see §8).'
          );
        }
      }
    };
    reader.readAsText(file);
  };

  const handleValidateImport = async (overrideMappings?: Record<string, string>) => {
    if (!importCsvText) {
      setImportGeneralError('Please select a valid CSV file.');
      return;
    }
    setValidatingImport(true);
    setImportGeneralError(null);
    try {
      const effectiveMappings = overrideMappings ?? controlMappings;
      const res = await authFetch(`/api/Financials/ImportAccounts/Validate?mode=${importMode}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          csvContent: importCsvText,
          mode: importMode,
          retireMissing: importMode === 'upsert' && retireMissing,
          controlMappings: effectiveMappings,
        }),
      });
      const data = (await res.json()) as CoaImportPreview;
      if (!res.ok) {
        throw new Error((data as unknown as { message?: string })?.message || 'Failed to validate CSV file.');
      }
      setImportPreview(data);
      if (data.controlMappings && data.controlMappings.length > 0) {
        setControlMappings((prev) => {
          const next = { ...prev };
          for (const m of data.controlMappings!) {
            if (!next[m.roleKey] && m.targetAccountCode) {
              next[m.roleKey] = m.targetAccountCode;
            }
          }
          return next;
        });
      }
      setImportStep('preview');
    } catch (err: unknown) {
      setImportGeneralError(err instanceof Error ? err.message : 'Validation failed.');
    } finally {
      setValidatingImport(false);
    }
  };

  const handleCommitImport = async () => {
    if (!importPreview || !importPreview.canCommit) return;
    setCommittingImport(true);
    setImportGeneralError(null);
    try {
      const res = await authFetch(`/api/Financials/ImportAccounts/Commit?mode=${importMode}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          csvContent: importCsvText,
          mode: importMode,
          retireMissing: importMode === 'upsert' && retireMissing,
          controlMappings,
        }),
      });
      if (!res.ok) {
        const errJson = (await res.json().catch(() => null)) as CoaImportPreview | null;
        if (errJson && errJson.rows) {
          setImportPreview(errJson);
          throw new Error('Import rejected due to validation errors.');
        }
        throw new Error('Failed to commit imported accounts.');
      }
      const result = (await res.json()) as CoaImportCommitResult;
      setImportResult(result);
      setImportStep('success');
      await load();
    } catch (err: unknown) {
      setImportGeneralError(err instanceof Error ? err.message : 'Failed to commit import.');
    } finally {
      setCommittingImport(false);
    }
  };

  const downloadErrorReport = () => {
    if (!importPreview) return;
    const header = ['Row', 'AccountCode', 'AccountName', 'Action', 'Errors', 'Warnings'];
    const lines = [header.map((h) => `"${h}"`).join(',')];
    if (importPreview.fileErrors && importPreview.fileErrors.length > 0) {
      for (const fe of importPreview.fileErrors) {
        lines.push(`"0","","","file_error","${fe.replace(/"/g, '""')}",""`);
      }
    }
    for (const r of importPreview.rows) {
      if (r.errors.length > 0 || r.warnings.length > 0) {
        const errStr = r.errors.join(' | ').replace(/"/g, '""');
        const warnStr = r.warnings.join(' | ').replace(/"/g, '""');
        lines.push(
          `"${r.rowNumber}","${r.accountCode}","${(r.accountName || '').replace(/"/g, '""')}","${r.action}","${errStr}","${warnStr}"`
        );
      }
    }
    const blob = new Blob([lines.join('\r\n')], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `coa-import-validation-report-${new Date().toISOString().slice(0, 10)}.csv`;
    link.click();
    URL.revokeObjectURL(url);
  };

  // Open dialogs
  const openAddAccount = (parent?: ApiAccount) => {
    setEditingAccount(null);
    setFormError(null);
    setFormData({
      accountCode: '',
      accountName: '',
      accountClassId: parent?.accountClassId || 1,
      parentAccountId: parent?.id || '',
      description: '',
      isCash: false,
      isContraAccount: false,
    });
    setEditorOpen(true);
  };

  const openEditAccount = (account: ApiAccount) => {
    setEditingAccount(account);
    setFormError(null);
    setFormData({
      accountCode: account.accountCode,
      accountName: account.accountName,
      accountClassId: account.accountClassId,
      parentAccountId: account.parentAccountId ?? '',
      description: account.description || '',
      isCash: account.isCash,
      isContraAccount: account.isContraAccount,
    });
    setEditorOpen(true);
  };

  // Save handler (Add/Update)
  const saveAccount = async (e: React.FormEvent) => {
    e.preventDefault();
    setFormError(null);

    const code = formData.accountCode.trim();
    const name = formData.accountName.trim();
    if (!code) {
      setFormError('Account code is required.');
      return;
    }
    if (!name) {
      setFormError('Account name is required.');
      return;
    }

    setSaving(true);
    try {
      const payload = {
        accountCode: code,
        accountName: name,
        accountClassId: Number(formData.accountClassId),
        parentAccountId: formData.parentAccountId === '' ? null : Number(formData.parentAccountId),
        description: formData.description.trim() || null,
        isCash: formData.isCash,
        isContraAccount: formData.isContraAccount,
      };

      let res: Response;
      if (editingAccount) {
        res = await authFetch(`/api/Financials/UpdateAccount/${encodeURIComponent(editingAccount.accountCode)}`, {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(payload),
        });
      } else {
        res = await authFetch('/api/Financials/AddAccount', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(payload),
        });
      }

      if (!res.ok) {
        const errJson = await res.json().catch(() => null) as { message?: string } | null;
        throw new Error(errJson?.message || `Failed to ${editingAccount ? 'update' : 'create'} account.`);
      }

      setEditorOpen(false);
      await load();
    } catch (err: unknown) {
      setFormError(err instanceof Error ? err.message : 'An error occurred while saving.');
    } finally {
      setSaving(false);
    }
  };

  // Lifecycle actions
  const handleArchiveToggle = async (account: ApiAccount) => {
    if (!account.archived && account.isProtected) {
      setActionBlocked({
        title: 'Cannot Archive Protected Account',
        message: account.protectionReason || 'This account is protected by system configurations or has child accounts.',
      });
      return;
    }

    setActionBusy(true);
    try {
      const endpoint = account.archived
        ? `/api/Financials/UnarchiveAccount/${encodeURIComponent(account.accountCode)}`
        : `/api/Financials/ArchiveAccount/${encodeURIComponent(account.accountCode)}`;
      const res = await authFetch(endpoint, { method: 'POST' });
      if (!res.ok) {
        const errJson = await res.json().catch(() => null) as { message?: string } | null;
        throw new Error(errJson?.message || `Failed to ${account.archived ? 'unarchive' : 'archive'} account.`);
      }
      await load();
    } catch (err: unknown) {
      setActionBlocked({
        title: account.archived ? 'Unarchive Failed' : 'Archive Failed',
        message: err instanceof Error ? err.message : 'Operation failed.',
      });
    } finally {
      setActionBusy(false);
    }
  };

  const handleDeleteRequest = (account: ApiAccount) => {
    if (!account.canDelete) {
      setActionBlocked({
        title: 'Cannot Delete Account',
        message: account.deleteBlockedReason || account.protectionReason || 'This account cannot be deleted because it is protected or has transaction history.',
      });
      return;
    }
    setDeleteConfirm(account);
  };

  const executeDelete = async () => {
    if (!deleteConfirm) return;
    setActionBusy(true);
    try {
      const res = await authFetch(`/api/Financials/DeleteAccount/${encodeURIComponent(deleteConfirm.accountCode)}`, {
        method: 'DELETE',
      });
      if (!res.ok) {
        const errText = await res.text().catch(() => 'Failed to delete account.');
        throw new Error(errText);
      }
      setDeleteConfirm(null);
      await load();
    } catch (err: unknown) {
      setActionBlocked({
        title: 'Delete Failed',
        message: err instanceof Error ? err.message : 'Could not delete account.',
      });
    } finally {
      setActionBusy(false);
    }
  };

  // Tree-grid keyboard support: Up/Down move between rows, Right expands, Left collapses or goes to the parent.
  const onRowKeyDown = (e: React.KeyboardEvent<HTMLTableRowElement>, row: Row, index: number) => {
    const focusRow = (i: number) =>
      tableRef.current?.querySelectorAll<HTMLTableRowElement>('tr[data-row]')[i]?.focus();
    const isOpen = !collapsed.has(row.account.id) || filtering;
    if (e.key === 'ArrowDown') focusRow(index + 1);
    else if (e.key === 'ArrowUp') focusRow(index - 1);
    else if (e.key === 'ArrowRight' && row.hasChildren && !isOpen) toggle(row.account.id, true);
    else if (e.key === 'ArrowLeft' && row.hasChildren && isOpen && !filtering) toggle(row.account.id, false);
    else if (e.key === 'ArrowLeft' && row.parentId !== null) focusRow(rows.findIndex((r) => r.account.id === row.parentId));
    else if ((e.key === 'Enter' || e.key === ' ') && row.hasChildren && !filtering) toggle(row.account.id);
    else return;
    e.preventDefault();
  };

  if (!hasPermission('financials.accounts.view')) {
    return <PermissionDeniedCard permission="financials.accounts.view" />;
  }

  const chip = (active: boolean) =>
    `h-8 px-3 rounded-full text-xs font-semibold transition-colors cursor-pointer ${
      active
        ? 'bg-[var(--brand-subtle)] text-[var(--brand-primary)] ring-1 ring-[var(--brand-ring)]'
        : 'bg-[var(--bg-subtle)] text-[var(--text-secondary)] hover:bg-[var(--bg-surface-hover)]'
    }`;

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow={
          <nav aria-label="Breadcrumb">
            <Link to={`/org/${currentOrg.slug}/financials`} className="hover:underline">General Ledger</Link>
            <span aria-hidden="true"> / </span>
            <span>Chart of Accounts</span>
          </nav>
        }
        title="Chart of Accounts"
        subtitle="Accounts in financial statement order. Group rows show the total of their sub-accounts."
        actions={
          <div className="flex items-center gap-2">
            <HeaderButton label="Refresh accounts" icon={RefreshCw} iconOnly busy={loading} onClick={load} />

            {/* Export Dropdown Menu */}
            <div className="relative" ref={exportDropdownRef}>
              <HeaderButton
                label="Export"
                icon={Download}
                compact
                disabled={allRows.length === 0}
                onClick={() => setExportMenuOpen((prev) => !prev)}
              />
              {exportMenuOpen && (
                <div
                  className="absolute right-0 mt-2 w-64 rounded-xl bg-[var(--bg-surface)] border border-[var(--border-subtle)] shadow-xl z-50 py-1.5 text-xs text-[var(--text-main)]"
                  role="menu"
                >
                  {canManage && (
                    <button
                      type="button"
                      role="menuitem"
                      className="w-full text-left px-3.5 py-2 hover:bg-[var(--bg-surface-hover)] flex items-center gap-2.5"
                      onClick={exportStructure}
                    >
                      <Download className="w-3.5 h-3.5 text-[var(--brand-primary)]" />
                      <div>
                        <div className="font-semibold">Export Structure</div>
                        <div className="text-[10px] text-[var(--text-muted)]">AccountGo-COA-v1 (no balances)</div>
                      </div>
                    </button>
                  )}
                  <button
                    type="button"
                    role="menuitem"
                    className="w-full text-left px-3.5 py-2 hover:bg-[var(--bg-surface-hover)] flex items-center gap-2.5"
                    onClick={exportSnapshot}
                  >
                    <Download className="w-3.5 h-3.5 text-emerald-600" />
                    <div>
                      <div className="font-semibold">Export Snapshot</div>
                      <div className="text-[10px] text-[var(--text-muted)]">Includes current balances</div>
                    </div>
                  </button>
                  <div className="my-1 border-t border-[var(--border-subtle)]" />
                  <button
                    type="button"
                    role="menuitem"
                    className="w-full text-left px-3.5 py-2 hover:bg-[var(--bg-surface-hover)] flex items-center gap-2.5"
                    onClick={downloadSampleTemplate}
                  >
                    <FileDown className="w-3.5 h-3.5 text-indigo-500" />
                    <div>
                      <div className="font-semibold">Sample Template</div>
                      <div className="text-[10px] text-[var(--text-muted)]">Download AccountGo Structure CSV</div>
                    </div>
                  </button>
                </div>
              )}
            </div>

            {canManage && (
              <HeaderButton
                label="Import"
                icon={Upload}
                compact
                onClick={() => openImportWizard('append')}
              />
            )}

            {canManage && (
              <HeaderButton
                label="Add Account"
                icon={Plus}
                variant="primary"
                compact
                onClick={() => openAddAccount()}
              />
            )}
          </div>
        }
      />

      {/* Empty-Books Banner (§7.4, §12 P3) */}
      {chartStatus?.isEmptyBooks && canManage && (
        <div
          data-testid="empty-books-banner"
          className="p-4 rounded-2xl bg-amber-50/70 dark:bg-amber-950/30 border border-amber-200/80 dark:border-amber-900/40 flex flex-col sm:flex-row items-start sm:items-center justify-between gap-4 shadow-sm"
        >
          <div className="flex items-start gap-3">
            <div className="p-2 rounded-xl bg-amber-100 dark:bg-amber-900/50 text-amber-700 dark:text-amber-300 shrink-0 mt-0.5">
              <Sparkles className="w-5 h-5" />
            </div>
            <div>
              <h4 className="text-sm font-semibold text-[var(--text-main)]">
                Books not started — Replace chart is available.
              </h4>
              <p className="text-xs text-[var(--text-secondary)] mt-0.5">
                You have no posted journal entries or transactions. You can safely replace this default chart of accounts with your own firm structure.
                {chartStatus.unpostedDraftCount > 0 && ` (${chartStatus.unpostedDraftCount} unposted draft journal(s) will be preserved)`}
              </p>
            </div>
          </div>
          {isOwner && (
            <button
              type="button"
              onClick={() => openImportWizard('replace')}
              className="px-3.5 py-1.5 rounded-xl bg-amber-600 hover:bg-amber-700 text-white text-xs font-semibold shrink-0 transition-colors shadow-sm"
            >
              Replace Chart
            </button>
          )}
        </div>
      )}

      <div className="p-4 rounded-2xl bg-[var(--bg-surface)] shadow-md space-y-3">
        <div className="flex flex-col md:flex-row md:items-center gap-3">
          <div className="relative w-full md:w-72">
            <Search className="w-4 h-4 text-[var(--text-muted)] absolute left-3 top-2.5 pointer-events-none" />
            <input
              type="search"
              aria-label="Search accounts by code or name"
              placeholder="Search code or name..."
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              className="w-full h-9 pl-9 pr-3 rounded-xl bg-[var(--bg-subtle)] text-xs text-[var(--text-main)] focus:outline-none focus:ring-1 focus:ring-[var(--brand-ring)]"
            />
          </div>
          <div className="flex flex-wrap items-center gap-2" role="group" aria-label="Filter by account class">
            <button type="button" aria-pressed={classFilter === null} className={chip(classFilter === null)} onClick={() => setClassFilter(null)}>
              All
            </button>
            {classes.map((id) => (
              <button key={id} type="button" aria-pressed={classFilter === id} className={chip(classFilter === id)} onClick={() => setClassFilter(id)}>
                {CLASS_LABELS[id] || `Class ${id}`}
              </button>
            ))}
          </div>
          <div className="md:ml-auto flex flex-wrap items-center gap-4 text-xs text-[var(--text-secondary)]">
            <label className="flex items-center gap-1.5 cursor-pointer select-none">
              <input type="checkbox" checked={hideZero} onChange={(e) => setHideZero(e.target.checked)} className="accent-[var(--brand-primary)]" />
              Hide zero balances
            </label>
            <label className="flex items-center gap-1.5 cursor-pointer select-none">
              <input type="checkbox" checked={hideArchived} onChange={(e) => setHideArchived(e.target.checked)} className="accent-[var(--brand-primary)]" />
              Hide archived
            </label>
          </div>
        </div>

        {loading && accounts.length === 0 ? (
          <div className="py-12 flex items-center justify-center gap-2 text-xs text-[var(--text-muted)]">
            <Loader2 className="w-4 h-4 animate-spin text-[var(--brand-primary)]" />
            Loading chart of accounts...
          </div>
        ) : loadFailed ? (
          <div role="alert" className="py-12 text-center text-xs text-[var(--text-muted)]">
            Could not load the chart of accounts. Try refreshing.
          </div>
        ) : rows.length === 0 ? (
          <div className="py-12 text-center text-xs text-[var(--text-muted)]">No accounts match these filters.</div>
        ) : (
          <div className="overflow-x-auto">
            <table role="treegrid" aria-label="Chart of accounts" className="w-full text-left text-xs border-collapse">
              <thead>
                <tr className="border-b border-[var(--border-subtle)] text-[var(--text-secondary)] font-medium">
                  <th scope="col" className="py-2.5 px-3 w-24">Code</th>
                  <th scope="col" className="py-2.5 px-3">Account</th>
                  <th scope="col" className="py-2.5 px-3 w-20 hidden sm:table-cell">Normal</th>
                  <th scope="col" className="py-2.5 px-3 w-32 text-right">Balance</th>
                  {canManage && <th scope="col" className="py-2.5 px-3 w-28 text-right">Actions</th>}
                </tr>
              </thead>
              <tbody ref={tableRef}>
                {rows.map((row, index) => {
                  const { account: a, level, rollup, hasChildren } = row;
                  const isRoot = level === 1;
                  const open = filtering || !collapsed.has(a.id);
                  const abnormal = isAbnormal(a, rollup);
                  const isArchived = !!a.archived;

                  return (
                    <tr
                      key={a.id}
                      data-row
                      tabIndex={index === 0 ? 0 : -1}
                      aria-level={level}
                      aria-expanded={hasChildren ? open : undefined}
                      onKeyDown={(e) => onRowKeyDown(e, row, index)}
                      className={`border-b border-[var(--border-subtle)] focus:outline-none focus:bg-[var(--bg-surface-hover)] ${
                        isArchived ? 'opacity-60 bg-gray-50/50 dark:bg-gray-900/20' : isRoot ? 'bg-[var(--bg-subtle)] font-bold text-[var(--text-primary)]' : hasChildren ? 'font-semibold text-[var(--text-main)]' : 'text-[var(--text-secondary)]'
                      }`}
                    >
                      <td className="py-2 px-3 font-mono whitespace-nowrap">{a.accountCode}</td>
                      <td className="py-2 px-3">
                        <div className="flex items-center gap-1.5 min-w-0" style={{ paddingLeft: `${(level - 1) * 1.25}rem` }}>
                          {hasChildren ? (
                            <button
                              type="button"
                              tabIndex={-1}
                              aria-label={`${open ? 'Collapse' : 'Expand'} ${a.accountName}`}
                              disabled={filtering}
                              onClick={() => toggle(a.id)}
                              className="p-0.5 rounded text-[var(--text-muted)] hover:text-[var(--text-primary)] cursor-pointer disabled:cursor-default"
                            >
                              {open ? <ChevronDown className="w-3.5 h-3.5" /> : <ChevronRight className="w-3.5 h-3.5" />}
                            </button>
                          ) : (
                            <span className="w-4.5 shrink-0" aria-hidden="true" />
                          )}
                          <span className={`truncate ${isArchived ? 'line-through text-[var(--text-muted)]' : ''}`}>{a.accountName}</span>
                          {a.isContraAccount && <Badge>Contra</Badge>}
                          {a.isCash && <Badge>Cash</Badge>}
                          {bankByAccountId.has(a.id) && (
                            <Link
                              to={`/org/${currentOrg.slug}/banking/${bankByAccountId.get(a.id)}`}
                              className="shrink-0"
                              title="Open bank register"
                            >
                              <Badge>Bank</Badge>
                            </Link>
                          )}
                          {hasChildren && !isRoot && <Badge muted>Header</Badge>}
                          {isArchived && <Badge variant="archived">Archived</Badge>}
                          {a.isProtected && (
                            <span title={a.protectionReason || 'Protected system account'}>
                              <Badge variant="control">Control</Badge>
                            </span>
                          )}
                        </div>
                      </td>
                      <td className="py-2 px-3 hidden sm:table-cell font-mono text-[var(--text-muted)]">
                        {a.normalBalance === 'NA' ? '—' : a.normalBalance}
                      </td>
                      <td className={`py-2 px-3 text-right font-mono whitespace-nowrap ${abnormal ? 'text-amber-500' : ''}`}>
                        {a.canPost && hasPermission('financials.journal.view')
                          ? <Link className="underline" title={`View ledger for ${a.accountCode}`}
                            to={`/org/${currentOrg.slug}/financials?${new URLSearchParams({ accountCode: a.accountCode, from: '', to: '' })}`}>{formatAmount(rollup)}</Link>
                          : formatAmount(rollup)}
                        {abnormal && <span className="sr-only"> (abnormal balance)</span>}
                        {abnormal && <span aria-hidden="true" title="Abnormal balance: opposite to the account's normal side"> !</span>}
                      </td>

                      {/* Row Actions */}
                      {canManage && (
                        <td className="py-1.5 px-3 text-right whitespace-nowrap">
                          <div className="inline-flex items-center gap-1 justify-end">
                            <button
                              type="button"
                              aria-label={`Edit ${a.accountCode}`}
                              title="Edit account"
                              onClick={() => openEditAccount(a)}
                              className="p-1 rounded text-[var(--text-muted)] hover:text-[var(--text-main)] hover:bg-[var(--bg-subtle)]"
                            >
                              <Edit2 className="w-3.5 h-3.5" />
                            </button>

                            <button
                              type="button"
                              aria-label={isArchived ? `Unarchive ${a.accountCode}` : `Archive ${a.accountCode}`}
                              title={isArchived ? 'Unarchive account' : a.isProtected ? `Cannot archive: ${a.protectionReason}` : 'Archive account'}
                              disabled={actionBusy}
                              onClick={() => handleArchiveToggle(a)}
                              className={`p-1 rounded ${
                                isArchived
                                  ? 'text-indigo-600 hover:bg-indigo-50 dark:hover:bg-indigo-950/40'
                                  : a.isProtected
                                    ? 'text-[var(--text-muted)] opacity-50 cursor-pointer'
                                    : 'text-[var(--text-muted)] hover:text-amber-600 hover:bg-amber-50 dark:hover:bg-amber-950/40'
                              }`}
                            >
                              {isArchived ? <ArchiveRestore className="w-3.5 h-3.5" /> : <Archive className="w-3.5 h-3.5" />}
                            </button>

                            <button
                              type="button"
                              aria-label={`Delete ${a.accountCode}`}
                              title={a.canDelete ? 'Delete account' : `Cannot delete: ${a.deleteBlockedReason || 'Protected account'}`}
                              disabled={actionBusy}
                              onClick={() => handleDeleteRequest(a)}
                              className={`p-1 rounded ${
                                a.canDelete
                                  ? 'text-[var(--text-muted)] hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-950/40'
                                  : 'text-[var(--text-muted)] opacity-50 cursor-pointer'
                              }`}
                            >
                              <Trash2 className="w-3.5 h-3.5" />
                            </button>
                          </div>
                        </td>
                      )}
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Add / Edit Account Modal Dialog */}
      <ModalShell
        open={editorOpen}
        onClose={() => {
          if (!saving) setEditorOpen(false);
        }}
        closeOnEscape={!saving}
        closeOnBackdrop={!saving}
        bare
        panelClassName="bg-[var(--bg-surface)] rounded-2xl shadow-2xl max-w-lg w-full border border-[var(--border-subtle)] p-6 space-y-4 max-h-[min(90vh,calc(100dvh-2rem))] overflow-y-auto"
      >
          <div className="space-y-4">
            <div className="flex items-center justify-between">
              <h2 className="text-base font-semibold text-[var(--text-main)]">
                {editingAccount ? `Edit Account (${editingAccount.accountCode})` : 'Add New Account'}
              </h2>
              <button
                type="button"
                onClick={() => setEditorOpen(false)}
                className="p-1 rounded-lg text-[var(--text-muted)] hover:text-[var(--text-main)]"
              >
                <X className="w-4 h-4" />
              </button>
            </div>

            {formError && (
              <div role="alert" className="p-3 rounded-xl bg-red-50 dark:bg-red-950/30 text-red-600 dark:text-red-400 text-xs">
                {formError}
              </div>
            )}

            <form onSubmit={saveAccount} className="space-y-4 text-xs">
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block font-medium text-[var(--text-secondary)] mb-1">
                    Account Code <span className="text-red-500">*</span>
                  </label>
                  <input
                    type="text"
                    required
                    value={formData.accountCode}
                    onChange={(e) => setFormData({ ...formData, accountCode: e.target.value })}
                    className="w-full h-8 px-2.5 rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-subtle)] text-[var(--text-main)] focus:outline-none focus:ring-1 focus:ring-[var(--brand-ring)]"
                    placeholder="e.g. 10115"
                  />
                </div>
                <div>
                  <label className="block font-medium text-[var(--text-secondary)] mb-1">
                    Account Class <span className="text-red-500">*</span>
                  </label>
                  <select
                    value={formData.accountClassId}
                    onChange={(e) => setFormData({ ...formData, accountClassId: Number(e.target.value) })}
                    className="w-full h-8 px-2.5 rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-subtle)] text-[var(--text-main)] focus:outline-none focus:ring-1 focus:ring-[var(--brand-ring)]"
                  >
                    {Object.entries(CLASS_LABELS).map(([id, label]) => (
                      <option key={id} value={id}>{id} - {label}</option>
                    ))}
                  </select>
                </div>
              </div>

              <div>
                <label className="block font-medium text-[var(--text-secondary)] mb-1">
                  Account Name <span className="text-red-500">*</span>
                </label>
                <input
                  type="text"
                  required
                  value={formData.accountName}
                  onChange={(e) => setFormData({ ...formData, accountName: e.target.value })}
                  className="w-full h-8 px-2.5 rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-subtle)] text-[var(--text-main)] focus:outline-none focus:ring-1 focus:ring-[var(--brand-ring)]"
                  placeholder="e.g. Payroll Checking Account"
                />
              </div>

              <div>
                <label className="block font-medium text-[var(--text-secondary)] mb-1">
                  Parent Account (Optional)
                </label>
                <select
                  value={formData.parentAccountId}
                  onChange={(e) => setFormData({ ...formData, parentAccountId: e.target.value === '' ? '' : Number(e.target.value) })}
                  className="w-full h-8 px-2.5 rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-subtle)] text-[var(--text-main)] focus:outline-none focus:ring-1 focus:ring-[var(--brand-ring)]"
                >
                  <option value="">None (Top level account under class)</option>
                  {allRows
                    .filter((r) => !editingAccount || r.account.id !== editingAccount.id)
                    .map(({ account: a }) => (
                      <option key={a.id} value={a.id}>
                        {a.accountCode} - {a.accountName} ({CLASS_LABELS[a.accountClassId] || `Class ${a.accountClassId}`})
                      </option>
                    ))}
                </select>
              </div>

              <div>
                <label className="block font-medium text-[var(--text-secondary)] mb-1">
                  Description (Optional)
                </label>
                <textarea
                  rows={2}
                  value={formData.description}
                  onChange={(e) => setFormData({ ...formData, description: e.target.value })}
                  className="w-full p-2 rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-subtle)] text-[var(--text-main)] focus:outline-none focus:ring-1 focus:ring-[var(--brand-ring)] resize-none"
                  placeholder="Additional context or notes..."
                />
              </div>

              <div className="flex items-center gap-6 pt-1">
                <label className="flex items-center gap-2 cursor-pointer select-none text-[var(--text-secondary)]">
                  <input
                    type="checkbox"
                    checked={formData.isCash}
                    onChange={(e) => setFormData({ ...formData, isCash: e.target.checked })}
                    className="accent-[var(--brand-primary)]"
                  />
                  Cash or Bank Account
                </label>
                <label className="flex items-center gap-2 cursor-pointer select-none text-[var(--text-secondary)]">
                  <input
                    type="checkbox"
                    checked={formData.isContraAccount}
                    onChange={(e) => setFormData({ ...formData, isContraAccount: e.target.checked })}
                    className="accent-[var(--brand-primary)]"
                  />
                  Contra Account
                </label>
              </div>

              <div className="flex items-center justify-end gap-2 pt-3 border-t border-[var(--border-subtle)]">
                <button
                  type="button"
                  onClick={() => setEditorOpen(false)}
                  className="h-8 px-3 rounded-lg border border-[var(--border-subtle)] text-[var(--text-secondary)] hover:bg-[var(--bg-subtle)]"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={saving}
                  className="h-8 px-4 rounded-lg bg-[var(--brand-primary)] font-semibold text-[var(--brand-text)] hover:opacity-90 disabled:opacity-50 flex items-center gap-1.5"
                >
                  {saving && <Loader2 className="w-3.5 h-3.5 animate-spin" />}
                  {editingAccount ? 'Update Account' : 'Create Account'}
                </button>
              </div>
            </form>
          </div>
      </ModalShell>

      {/* Action Blocked Dialog */}
      <ModalShell
        open={!!actionBlocked}
        onClose={() => setActionBlocked(null)}
        maxWidthClassName="max-w-md"
        panelClassName="p-6 sm:p-6 space-y-4 shadow-2xl"
      >
          {actionBlocked && (
          <div className="space-y-4">
            <div className="flex items-start gap-3">
              <div className="p-2 rounded-xl bg-amber-50 dark:bg-amber-950/40 text-amber-600 dark:text-amber-400">
                <Shield className="w-5 h-5" />
              </div>
              <div className="space-y-1">
                <h3 className="text-sm font-semibold text-[var(--text-main)]">{actionBlocked.title}</h3>
                <p className="text-xs text-[var(--text-secondary)] leading-relaxed">{actionBlocked.message}</p>
              </div>
            </div>
            <div className="flex justify-end pt-2">
              <button
                type="button"
                onClick={() => setActionBlocked(null)}
                className="h-8 px-4 rounded-lg bg-[var(--bg-subtle)] text-xs font-semibold text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)]"
              >
                Understood
              </button>
            </div>
          </div>
          )}
      </ModalShell>

      {/* Safe Delete vs Archive Confirmation Modal (§5.4) */}
      <ModalShell
        open={!!deleteConfirm}
        onClose={() => setDeleteConfirm(null)}
        maxWidthClassName="max-w-md"
        panelClassName="p-6 sm:p-6 space-y-4 shadow-2xl"
      >
          {deleteConfirm && (
          <div className="space-y-4">
            <div className="flex items-start gap-3">
              <div className="p-2 rounded-xl bg-red-50 dark:bg-red-950/40 text-red-600 dark:text-red-400">
                <Trash2 className="w-5 h-5" />
              </div>
              <div className="space-y-1">
                <h3 className="text-sm font-semibold text-[var(--text-main)]">
                  Delete Account {deleteConfirm.accountCode}?
                </h3>
                <p className="text-xs text-[var(--text-secondary)] leading-relaxed">
                  Permanently deleting <strong>{deleteConfirm.accountName}</strong> will remove it from the chart.
                  If you want to retire this account while preserving historical data and integrity, we recommend archiving it instead.
                </p>
              </div>
            </div>
            <div className="flex flex-col sm:flex-row items-center justify-end gap-2 pt-2 border-t border-[var(--border-subtle)]">
              <button
                type="button"
                onClick={() => setDeleteConfirm(null)}
                className="w-full sm:w-auto h-8 px-3 rounded-lg border border-[var(--border-subtle)] text-xs text-[var(--text-secondary)] hover:bg-[var(--bg-subtle)]"
              >
                Cancel
              </button>
              <button
                type="button"
                onClick={async () => {
                  const target = deleteConfirm;
                  setDeleteConfirm(null);
                  await handleArchiveToggle(target);
                }}
                className="w-full sm:w-auto h-8 px-3 rounded-lg bg-amber-50 dark:bg-amber-950/40 text-amber-700 dark:text-amber-300 text-xs font-semibold hover:bg-amber-100 dark:hover:bg-amber-900/40"
              >
                Archive Instead (Recommended)
              </button>
              <button
                type="button"
                disabled={actionBusy}
                onClick={executeDelete}
                className="w-full sm:w-auto h-8 px-3 rounded-lg bg-red-600 text-white text-xs font-semibold hover:bg-red-700 disabled:opacity-50 flex items-center justify-center gap-1.5"
              >
                {actionBusy && <Loader2 className="w-3.5 h-3.5 animate-spin" />}
                Permanently Delete
              </button>
            </div>
          </div>
          )}
      </ModalShell>

      {/* Import Wizard Modal (§7, §9, §12 P2) */}
      <ModalShell
        open={importModalOpen}
        onClose={() => setImportModalOpen(false)}
        bare
        panelClassName="bg-[var(--bg-surface)] rounded-2xl shadow-2xl max-w-3xl w-full border border-[var(--border-subtle)] overflow-hidden flex flex-col max-h-[min(90vh,calc(100dvh-2rem))]"
      >
          <div className="flex flex-col min-h-0 flex-1">
            {/* Modal Header */}
            <div className="px-6 py-4 border-b border-[var(--border-subtle)] flex items-center justify-between">
              <div className="flex items-center gap-2.5">
                <div className="p-2 rounded-xl bg-[var(--brand-subtle)] text-[var(--brand-primary)]">
                  <Upload className="w-5 h-5" />
                </div>
                <div>
                  <h3 className="text-base font-semibold text-[var(--text-main)]">
                    {importStep === 'upload' && 'Import Chart of Accounts'}
                    {importStep === 'preview' && 'Review & Validate Import'}
                    {importStep === 'success' && 'Import Completed'}
                  </h3>
                  <p className="text-xs text-[var(--text-secondary)]">
                    {importStep === 'upload' && 'Upload a structure CSV formatted as AccountGo-COA-v1.'}
                    {importStep === 'preview' && 'Inspect actions, safety checks, and error reports before applying changes.'}
                    {importStep === 'success' && 'Accounts have been successfully saved to your chart of accounts.'}
                  </p>
                </div>
              </div>
              <button
                type="button"
                onClick={() => setImportModalOpen(false)}
                className="p-1.5 rounded-lg text-[var(--text-muted)] hover:text-[var(--text-main)] hover:bg-[var(--bg-subtle)]"
              >
                <X className="w-5 h-5" />
              </button>
            </div>

            {/* Modal Body */}
            <div className="p-6 overflow-y-auto flex-1 space-y-5 text-xs text-[var(--text-main)]">
              {importGeneralError && (
                <div className="p-3 rounded-xl bg-red-50 dark:bg-red-950/40 border border-red-200 dark:border-red-900/40 text-red-700 dark:text-red-300 flex items-start gap-2">
                  <AlertCircle className="w-4 h-4 shrink-0 mt-0.5" />
                  <div>{importGeneralError}</div>
                </div>
              )}

              {/* STEP 1: UPLOAD & MODE */}
              {importStep === 'upload' && (
                <div className="space-y-5">
                  {/* Mode Selector */}
                  <div>
                    <label className="block font-semibold text-[var(--text-secondary)] uppercase text-[10px] tracking-wider mb-2">
                      Import Mode
                    </label>
                    <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
                      <label
                        className={`p-3.5 rounded-xl border cursor-pointer transition-colors flex flex-col gap-1.5 ${
                          importMode === 'append'
                            ? 'border-[var(--brand-primary)] bg-[var(--brand-subtle)]/30 ring-1 ring-[var(--brand-primary)]'
                            : 'border-[var(--border-subtle)] bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)]'
                        }`}
                      >
                        <div className="flex items-center justify-between">
                          <span className="font-semibold text-sm text-[var(--text-main)]">Append</span>
                          <input
                            type="radio"
                            name="importMode"
                            value="append"
                            checked={importMode === 'append'}
                            onChange={() => setImportMode('append')}
                            className="accent-[var(--brand-primary)]"
                          />
                        </div>
                        <p className="text-xs text-[var(--text-secondary)] leading-relaxed">
                          Add new accounts only. If an account code already exists in your chart, it is safely skipped without modification.
                        </p>
                      </label>

                      <label
                        className={`p-3.5 rounded-xl border cursor-pointer transition-colors flex flex-col gap-1.5 ${
                          importMode === 'upsert'
                            ? 'border-[var(--brand-primary)] bg-[var(--brand-subtle)]/30 ring-1 ring-[var(--brand-primary)]'
                            : 'border-[var(--border-subtle)] bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)]'
                        }`}
                      >
                        <div className="flex items-center justify-between">
                          <span className="font-semibold text-sm text-[var(--text-main)]">Upsert</span>
                          <input
                            type="radio"
                            name="importMode"
                            value="upsert"
                            checked={importMode === 'upsert'}
                            onChange={() => setImportMode('upsert')}
                            className="accent-[var(--brand-primary)]"
                          />
                        </div>
                        <p className="text-xs text-[var(--text-secondary)] leading-relaxed">
                          Add new accounts and update existing ones (names, parent hierarchy, cash/contra flags). Class changes on accounts with history are blocked.
                        </p>
                      </label>

                      <label
                        className={`p-3.5 rounded-xl border transition-colors flex flex-col gap-1.5 ${
                          !isOwner
                            ? 'opacity-50 cursor-not-allowed border-[var(--border-subtle)] bg-[var(--bg-subtle)]'
                            : chartStatus?.isEmptyBooks === false
                            ? 'opacity-50 cursor-not-allowed border-[var(--border-subtle)] bg-[var(--bg-subtle)]'
                            : importMode === 'replace'
                            ? 'border-amber-500 bg-amber-50/40 dark:bg-amber-950/20 ring-1 ring-amber-500 cursor-pointer'
                            : 'border-[var(--border-subtle)] bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)] cursor-pointer'
                        }`}
                      >
                        <div className="flex items-center justify-between">
                          <div className="flex items-center gap-1.5">
                            <span className="font-semibold text-sm text-[var(--text-main)]">Replace</span>
                            <span className="px-1.5 py-0.5 rounded text-[9px] font-bold uppercase tracking-wide bg-amber-100 dark:bg-amber-950 text-amber-800 dark:text-amber-300">
                              Empty Books
                            </span>
                          </div>
                          <input
                            type="radio"
                            name="importMode"
                            value="replace"
                            disabled={!isOwner || chartStatus?.isEmptyBooks === false}
                            checked={importMode === 'replace'}
                            onChange={() => setImportMode('replace')}
                            className="accent-amber-600"
                          />
                        </div>
                        <p className="text-xs text-[var(--text-secondary)] leading-relaxed">
                          Replaces chart with uploaded structure, remaps control roles, and removes unreferenced default accounts.
                        </p>
                        {chartStatus?.isEmptyBooks === false && (
                          <div className="text-[10px] text-rose-600 dark:text-rose-400 font-medium">
                            Locked: {chartStatus.postedTransactionsCount} posted transaction(s) exist.
                          </div>
                        )}
                        {!isOwner && (
                          <div className="text-[10px] text-amber-700 dark:text-amber-300 font-medium">
                            Locked: Owner role required.
                          </div>
                        )}
                      </label>
                    </div>

                    {/* Retire-missing checkbox on Upsert (§7.4, §9, §12 P3) */}
                    {importMode === 'upsert' && isOwner && (
                      <div className="p-3 rounded-xl bg-[var(--bg-subtle)] border border-[var(--border-subtle)] mt-3">
                        <label className="flex items-start gap-2.5 cursor-pointer select-none">
                          <input
                            type="checkbox"
                            checked={retireMissing}
                            onChange={(e) => setRetireMissing(e.target.checked)}
                            className="mt-0.5 accent-[var(--brand-primary)]"
                          />
                          <div>
                            <span className="font-semibold text-xs text-[var(--text-main)]">
                              Retire missing accounts
                            </span>
                            <p className="text-[11px] text-[var(--text-secondary)] leading-relaxed mt-0.5">
                              Archive any active accounts in your chart that are omitted from the CSV. Protected control accounts are never retired.
                            </p>
                          </div>
                        </label>
                      </div>
                    )}
                  </div>

                  {/* Dropzone */}
                  <div>
                    <label className="block font-semibold text-[var(--text-secondary)] uppercase text-[10px] tracking-wider mb-2">
                      CSV File (AccountGo-COA-v1)
                    </label>
                    <input
                      ref={importFileInputRef}
                      type="file"
                      accept=".csv,text/csv"
                      className="hidden"
                      onChange={(e) => e.target.files?.[0] && handleFileSelect(e.target.files[0])}
                    />
                    <div
                      onDragOver={(e) => e.preventDefault()}
                      onDrop={(e) => {
                        e.preventDefault();
                        if (e.dataTransfer.files?.[0]) handleFileSelect(e.dataTransfer.files[0]);
                      }}
                      onClick={() => importFileInputRef.current?.click()}
                      className="border-2 border-dashed border-[var(--border-subtle)] rounded-2xl p-6 flex flex-col items-center justify-center gap-2 cursor-pointer hover:border-[var(--brand-primary)] hover:bg-[var(--bg-subtle)]/50 transition-colors"
                    >
                      <FileText className="w-8 h-8 text-[var(--text-muted)]" />
                      <div className="text-center">
                        <div className="font-semibold text-sm text-[var(--text-main)]">
                          {importFile ? importFile.name : 'Choose a CSV file or drag and drop here'}
                        </div>
                        <div className="text-[11px] text-[var(--text-secondary)] mt-0.5">
                          {importFile
                            ? `${(importFile.size / 1024).toFixed(1)} KB — Click to choose a different file`
                            : 'Standard UTF-8 CSV with AccountGo-COA-v1 header'}
                        </div>
                      </div>
                    </div>
                  </div>

                  {/* Client Warning for Balance Columns (§7.1 / §8) */}
                  {importClientWarning && (
                    <div className="p-3.5 rounded-xl bg-amber-50 dark:bg-amber-950/40 border border-amber-200 dark:border-amber-900/40 text-amber-800 dark:text-amber-200 flex items-start gap-2.5">
                      <AlertTriangle className="w-5 h-5 shrink-0 text-amber-600 dark:text-amber-400 mt-0.5" />
                      <div className="space-y-1">
                        <div className="font-semibold text-xs">Opening Balances Not Supported in Structure Import</div>
                        <p className="text-xs text-amber-700 dark:text-amber-300 leading-relaxed">
                          {importClientWarning}
                        </p>
                      </div>
                    </div>
                  )}

                  {/* Template download link */}
                  <div className="flex items-center justify-between p-3 rounded-xl bg-[var(--bg-subtle)] text-xs">
                    <span className="text-[var(--text-secondary)]">Need a starting structure format?</span>
                    <button
                      type="button"
                      onClick={downloadSampleTemplate}
                      className="font-semibold text-[var(--brand-primary)] hover:underline flex items-center gap-1"
                    >
                      <Download className="w-3.5 h-3.5" />
                      Download Sample Template
                    </button>
                  </div>
                </div>
              )}

              {/* STEP 2: PREVIEW & VALIDATION */}
              {importStep === 'preview' && importPreview && (
                <div className="space-y-4">
                  {/* Summary Metric Pills */}
                  <div className="grid grid-cols-2 sm:grid-cols-6 gap-2">
                    <div className="p-2.5 rounded-xl bg-[var(--bg-subtle)] text-center">
                      <div className="text-base font-bold text-[var(--text-main)]">{importPreview.totalRows}</div>
                      <div className="text-[10px] uppercase font-semibold text-[var(--text-secondary)]">Total Rows</div>
                    </div>
                    <div className="p-2.5 rounded-xl bg-emerald-50 dark:bg-emerald-950/40 text-center">
                      <div className="text-base font-bold text-emerald-600 dark:text-emerald-400">{importPreview.createCount}</div>
                      <div className="text-[10px] uppercase font-semibold text-emerald-700 dark:text-emerald-300">Create</div>
                    </div>
                    {importMode === 'replace' ? (
                      <div className="p-2.5 rounded-xl bg-rose-50 dark:bg-rose-950/40 text-center">
                        <div className="text-base font-bold text-rose-600 dark:text-rose-400">{importPreview.deleteCount ?? 0}</div>
                        <div className="text-[10px] uppercase font-semibold text-rose-700 dark:text-rose-300">Delete (Old)</div>
                      </div>
                    ) : (
                      <div className="p-2.5 rounded-xl bg-indigo-50 dark:bg-indigo-950/40 text-center">
                        <div className="text-base font-bold text-indigo-600 dark:text-indigo-400">{importPreview.updateCount}</div>
                        <div className="text-[10px] uppercase font-semibold text-indigo-700 dark:text-indigo-300">Update</div>
                      </div>
                    )}
                    {importMode === 'replace' ? (
                      <div className="p-2.5 rounded-xl bg-amber-50 dark:bg-amber-950/40 text-center">
                        <div className="text-base font-bold text-amber-600 dark:text-amber-400">{importPreview.controlMappings?.length ?? 0}</div>
                        <div className="text-[10px] uppercase font-semibold text-amber-700 dark:text-amber-300">Remap Roles</div>
                      </div>
                    ) : importMode === 'upsert' && retireMissing ? (
                      <div className="p-2.5 rounded-xl bg-amber-50 dark:bg-amber-950/40 text-center">
                        <div className="text-base font-bold text-amber-600 dark:text-amber-400">{importPreview.retireCount ?? 0}</div>
                        <div className="text-[10px] uppercase font-semibold text-amber-700 dark:text-amber-300">Retire (Archive)</div>
                      </div>
                    ) : (
                      <div className="p-2.5 rounded-xl bg-amber-50 dark:bg-amber-950/40 text-center">
                        <div className="text-base font-bold text-amber-600 dark:text-amber-400">{importPreview.skipCount}</div>
                        <div className="text-[10px] uppercase font-semibold text-amber-700 dark:text-amber-300">Skip</div>
                      </div>
                    )}
                    <div className="p-2.5 rounded-xl bg-rose-50 dark:bg-rose-950/40 text-center">
                      <div className="text-base font-bold text-rose-600 dark:text-rose-400">
                        {importPreview.errorCount + importPreview.fileErrors.length}
                      </div>
                      <div className="text-[10px] uppercase font-semibold text-rose-700 dark:text-rose-300">Errors</div>
                    </div>
                    <div className="p-2.5 rounded-xl bg-orange-50 dark:bg-orange-950/40 text-center">
                      <div className="text-base font-bold text-orange-600 dark:text-orange-400">{importPreview.warningCount}</div>
                      <div className="text-[10px] uppercase font-semibold text-orange-700 dark:text-orange-300">Warnings</div>
                    </div>
                  </div>

                  {/* Control Account Remapping Grid (§7.4, §12 P3) */}
                  {importMode === 'replace' && importPreview.controlMappings && importPreview.controlMappings.length > 0 && (
                    <div className="p-4 rounded-xl border border-[var(--brand-primary)]/40 bg-[var(--brand-subtle)]/10 space-y-3">
                      <div className="flex items-center justify-between">
                        <div className="flex items-center gap-2">
                          <Shield className="w-4 h-4 text-[var(--brand-primary)]" />
                          <h4 className="font-semibold text-xs text-[var(--text-main)]">
                            Required Control Account Remapping ({importPreview.controlMappings.length} roles)
                          </h4>
                        </div>
                        <button
                          type="button"
                          onClick={() => handleValidateImport(controlMappings)}
                          className="text-[11px] font-semibold text-[var(--brand-primary)] hover:underline flex items-center gap-1"
                        >
                          <RefreshCw className="w-3 h-3" />
                          Re-validate Remappings
                        </button>
                      </div>
                      <p className="text-[11px] text-[var(--text-secondary)] leading-relaxed">
                        Replacing the chart requires remapping all system control accounts (General Ledger settings, active/inactive Taxes, active/inactive Banks) to your new chart accounts before import can be confirmed.
                      </p>

                      <div className="border border-[var(--border-subtle)] rounded-lg overflow-hidden bg-[var(--bg-surface)]">
                        <table className="w-full text-left border-collapse text-xs">
                          <thead className="bg-[var(--bg-subtle)] text-[10px] font-semibold uppercase text-[var(--text-secondary)] border-b border-[var(--border-subtle)]">
                            <tr>
                              <th className="py-2 px-3 w-28">Category</th>
                              <th className="py-2 px-3">Role / Service</th>
                              <th className="py-2 px-3">Current Seeded Account</th>
                              <th className="py-2 px-3 w-72">New Chart Account <span className="text-red-500">*</span></th>
                            </tr>
                          </thead>
                          <tbody className="divide-y divide-[var(--border-subtle)]">
                            {importPreview.controlMappings.map((mapping) => {
                              const selectedCode = controlMappings[mapping.roleKey] || '';
                              const isMapped = !!selectedCode;
                              return (
                                <tr key={mapping.roleKey} className="hover:bg-[var(--bg-subtle)]/40">
                                  <td className="py-2 px-3">
                                    <span className="px-1.5 py-0.5 rounded text-[9px] font-bold uppercase tracking-wide bg-[var(--bg-subtle)] text-[var(--text-secondary)]">
                                      {mapping.category}
                                    </span>
                                  </td>
                                  <td className="py-2 px-3 font-medium text-[var(--text-main)]">
                                    {mapping.roleTitle}
                                  </td>
                                  <td className="py-2 px-3 text-[var(--text-secondary)] font-mono text-[11px]">
                                    {mapping.currentAccountCode} – {mapping.currentAccountName}
                                  </td>
                                  <td className="py-2 px-3">
                                    <select
                                      aria-label={`Remap ${mapping.roleTitle}`}
                                      value={selectedCode}
                                      onChange={(e) => {
                                        const val = e.target.value;
                                        setControlMappings((prev) => ({ ...prev, [mapping.roleKey]: val }));
                                      }}
                                      className={`w-full h-7 px-2 rounded-lg border text-xs bg-[var(--bg-subtle)] text-[var(--text-main)] focus:outline-none focus:ring-1 focus:ring-[var(--brand-ring)] ${
                                        isMapped ? 'border-[var(--border-subtle)]' : 'border-rose-400 dark:border-rose-700'
                                      }`}
                                    >
                                      <option value="">-- Select Target Account --</option>
                                      {importPreview.rows
                                        .filter((r) => r.action !== 'error')
                                        .map((r) => (
                                          <option key={r.accountCode} value={r.accountCode}>
                                            {r.accountCode} - {r.accountName} ({r.accountClassName || `Class ${r.accountClassId}`})
                                          </option>
                                        ))}
                                    </select>
                                  </td>
                                </tr>
                              );
                            })}
                          </tbody>
                        </table>
                      </div>
                    </div>
                  )}

                  {/* File Level Errors */}
                  {importPreview.fileErrors.length > 0 && (
                    <div className="p-3.5 rounded-xl bg-rose-50 dark:bg-rose-950/40 border border-rose-200 dark:border-rose-900/40 text-rose-700 dark:text-rose-300 space-y-1">
                      <div className="font-semibold text-xs flex items-center gap-1.5">
                        <AlertCircle className="w-4 h-4" />
                        File Format Errors ({importPreview.fileErrors.length})
                      </div>
                      <ul className="list-disc pl-5 space-y-0.5 text-xs">
                        {importPreview.fileErrors.map((fe, i) => (
                          <li key={i}>{fe}</li>
                        ))}
                      </ul>
                    </div>
                  )}

                  {/* File Level Warnings */}
                  {importPreview.fileWarnings.length > 0 && (
                    <div className="p-3 rounded-xl bg-amber-50 dark:bg-amber-950/40 border border-amber-200 dark:border-amber-900/40 text-amber-800 dark:text-amber-200 space-y-1">
                      <div className="font-semibold text-xs flex items-center gap-1.5">
                        <AlertTriangle className="w-4 h-4 text-amber-600 dark:text-amber-400" />
                        Notices
                      </div>
                      <ul className="list-disc pl-5 space-y-0.5 text-xs">
                        {importPreview.fileWarnings.map((fw, i) => (
                          <li key={i}>{fw}</li>
                        ))}
                      </ul>
                    </div>
                  )}

                  {/* Row filter & Download error report */}
                  <div className="flex flex-wrap items-center justify-between gap-2 pt-1">
                    <div className="flex items-center gap-1">
                      {[
                        { id: 'all', label: `All (${importPreview.rows.length})` },
                        { id: 'create', label: `Create (${importPreview.createCount})` },
                        { id: 'update', label: `Update (${importPreview.updateCount})` },
                        { id: 'skip', label: `Skip (${importPreview.skipCount})` },
                        { id: 'error', label: `Error (${importPreview.errorCount})` },
                      ].map((tab) => (
                        <button
                          key={tab.id}
                          type="button"
                          onClick={() => setImportFilterAction(tab.id)}
                          className={`px-2.5 py-1 rounded-lg text-xs font-medium transition-colors ${
                            importFilterAction === tab.id
                              ? 'bg-[var(--brand-subtle)] text-[var(--brand-primary)]'
                              : 'text-[var(--text-secondary)] hover:bg-[var(--bg-subtle)]'
                          }`}
                        >
                          {tab.label}
                        </button>
                      ))}
                    </div>

                    {(importPreview.errorCount > 0 || importPreview.warningCount > 0 || importPreview.fileErrors.length > 0) && (
                      <button
                        type="button"
                        onClick={downloadErrorReport}
                        className="px-2.5 py-1 rounded-lg border border-[var(--border-subtle)] text-xs text-[var(--text-secondary)] hover:bg-[var(--bg-subtle)] flex items-center gap-1.5"
                      >
                        <Download className="w-3.5 h-3.5" />
                        Download Error Report
                      </button>
                    )}
                  </div>

                  {/* Rows Preview Table */}
                  <div className="border border-[var(--border-subtle)] rounded-xl overflow-hidden max-h-72 overflow-y-auto">
                    <table className="w-full text-left border-collapse text-xs">
                      <thead className="bg-[var(--bg-subtle)] sticky top-0 text-[10px] font-semibold uppercase text-[var(--text-secondary)] border-b border-[var(--border-subtle)]">
                        <tr>
                          <th className="py-2 px-2.5 w-12">#</th>
                          <th className="py-2 px-2.5 w-20">Code</th>
                          <th className="py-2 px-2.5">Name</th>
                          <th className="py-2 px-2.5 w-24">Class</th>
                          <th className="py-2 px-2.5 w-20">Parent</th>
                          <th className="py-2 px-2.5 w-20">Action</th>
                          <th className="py-2 px-2.5">Notes & Safety Checks</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-[var(--border-subtle)]">
                        {importPreview.rows
                          .filter((r) => importFilterAction === 'all' || r.action === importFilterAction)
                          .map((row) => {
                            let actionBadge = (
                              <span className="px-1.5 py-0.5 rounded text-[9px] font-bold uppercase tracking-wide bg-emerald-100 dark:bg-emerald-950 text-emerald-700 dark:text-emerald-300">
                                Create
                              </span>
                            );
                            if (row.action === 'update') {
                              actionBadge = (
                                <span className="px-1.5 py-0.5 rounded text-[9px] font-bold uppercase tracking-wide bg-indigo-100 dark:bg-indigo-950 text-indigo-700 dark:text-indigo-300">
                                  Update
                                </span>
                              );
                            } else if (row.action === 'skip') {
                              actionBadge = (
                                <span className="px-1.5 py-0.5 rounded text-[9px] font-bold uppercase tracking-wide bg-amber-100 dark:bg-amber-950 text-amber-700 dark:text-amber-300">
                                  Skip
                                </span>
                              );
                            } else if (row.action === 'error') {
                              actionBadge = (
                                <span className="px-1.5 py-0.5 rounded text-[9px] font-bold uppercase tracking-wide bg-rose-100 dark:bg-rose-950 text-rose-700 dark:text-rose-300">
                                  Error
                                </span>
                              );
                            }

                            return (
                              <tr key={row.rowNumber} className="hover:bg-[var(--bg-subtle)]/50">
                                <td className="py-2 px-2.5 text-[var(--text-muted)] font-mono">{row.rowNumber}</td>
                                <td className="py-2 px-2.5 font-mono font-medium">{row.accountCode}</td>
                                <td className="py-2 px-2.5 font-medium">{row.accountName}</td>
                                <td className="py-2 px-2.5 text-[var(--text-secondary)]">{row.accountClassName}</td>
                                <td className="py-2 px-2.5 font-mono text-[var(--text-secondary)]">{row.parentAccountCode || '—'}</td>
                                <td className="py-2 px-2.5">{actionBadge}</td>
                                <td className="py-2 px-2.5 space-y-0.5">
                                  {row.errors.length > 0 && (
                                    <div className="text-rose-600 dark:text-rose-400 font-medium">
                                      {row.errors.join('; ')}
                                    </div>
                                  )}
                                  {row.warnings.length > 0 && (
                                    <div className="text-amber-600 dark:text-amber-400">
                                      {row.warnings.join('; ')}
                                    </div>
                                  )}
                                  {row.errors.length === 0 && row.warnings.length === 0 && (
                                    <span className="text-[var(--text-muted)]">Ready</span>
                                  )}
                                </td>
                              </tr>
                            );
                          })}
                      </tbody>
                    </table>
                  </div>
                </div>
              )}

              {/* STEP 3: SUCCESS */}
              {importStep === 'success' && importResult && (
                <div className="py-8 flex flex-col items-center justify-center text-center space-y-4">
                  <div className="w-14 h-14 rounded-full bg-emerald-100 dark:bg-emerald-950/60 text-emerald-600 dark:text-emerald-400 flex items-center justify-center">
                    <CheckCircle2 className="w-8 h-8" />
                  </div>
                  <div className="space-y-1">
                    <h4 className="text-lg font-semibold text-[var(--text-main)]">Import Succeeded</h4>
                    <p className="text-xs text-[var(--text-secondary)]">
                      Chart of accounts successfully updated in <strong className="uppercase">{importResult.mode}</strong> mode.
                    </p>
                  </div>
                  <div className="flex flex-wrap justify-center gap-3 w-full max-w-lg pt-2">
                    <div className="p-3 rounded-xl bg-[var(--bg-subtle)] min-w-[90px]">
                      <div className="text-lg font-bold text-emerald-600 dark:text-emerald-400">{importResult.createdCount}</div>
                      <div className="text-[10px] uppercase font-semibold text-[var(--text-secondary)]">Created</div>
                    </div>
                    {importResult.mode !== 'replace' && (
                      <div className="p-3 rounded-xl bg-[var(--bg-subtle)] min-w-[90px]">
                        <div className="text-lg font-bold text-indigo-600 dark:text-indigo-400">{importResult.updatedCount}</div>
                        <div className="text-[10px] uppercase font-semibold text-[var(--text-secondary)]">Updated</div>
                      </div>
                    )}
                    {importResult.mode !== 'replace' && (
                      <div className="p-3 rounded-xl bg-[var(--bg-subtle)] min-w-[90px]">
                        <div className="text-lg font-bold text-amber-600 dark:text-amber-400">{importResult.skippedCount}</div>
                        <div className="text-[10px] uppercase font-semibold text-[var(--text-secondary)]">Skipped</div>
                      </div>
                    )}
                    {(importResult.deletedCount ?? 0) > 0 && (
                      <div className="p-3 rounded-xl bg-[var(--bg-subtle)] min-w-[90px]">
                        <div className="text-lg font-bold text-rose-600 dark:text-rose-400">{importResult.deletedCount}</div>
                        <div className="text-[10px] uppercase font-semibold text-[var(--text-secondary)]">Deleted</div>
                      </div>
                    )}
                    {(importResult.retiredCount ?? 0) > 0 && (
                      <div className="p-3 rounded-xl bg-[var(--bg-subtle)] min-w-[90px]">
                        <div className="text-lg font-bold text-amber-600 dark:text-amber-400">{importResult.retiredCount}</div>
                        <div className="text-[10px] uppercase font-semibold text-[var(--text-secondary)]">Retired</div>
                      </div>
                    )}
                  </div>
                </div>
              )}
            </div>

            {/* Modal Footer */}
            <div className="px-6 py-3.5 border-t border-[var(--border-subtle)] bg-[var(--bg-subtle)]/40 flex items-center justify-between">
              {importStep === 'upload' && (
                <>
                  <button
                    type="button"
                    onClick={() => setImportModalOpen(false)}
                    className="h-8 px-3 rounded-lg border border-[var(--border-subtle)] text-[var(--text-secondary)] hover:bg-[var(--bg-surface)] text-xs"
                  >
                    Cancel
                  </button>
                  <button
                    type="button"
                    disabled={!importFile || validatingImport || !!importClientWarning}
                    onClick={() => handleValidateImport()}
                    className="h-8 px-4 rounded-lg bg-[var(--brand-primary)] font-semibold text-[var(--brand-text)] hover:opacity-90 disabled:opacity-50 text-xs flex items-center gap-1.5"
                  >
                    {validatingImport && <Loader2 className="w-3.5 h-3.5 animate-spin" />}
                    Inspect & Preview
                  </button>
                </>
              )}

              {importStep === 'preview' && importPreview && (
                <>
                  <button
                    type="button"
                    onClick={() => setImportStep('upload')}
                    className="h-8 px-3 rounded-lg border border-[var(--border-subtle)] text-[var(--text-secondary)] hover:bg-[var(--bg-surface)] text-xs flex items-center gap-1"
                  >
                    <ArrowLeft className="w-3.5 h-3.5" />
                    Back / Select File
                  </button>
                  <div className="flex items-center gap-2">
                    {importPreview.hasErrors && (
                      <span className="text-[11px] text-rose-600 dark:text-rose-400 font-medium">
                        Resolve errors to proceed
                      </span>
                    )}
                    {!importPreview.hasErrors && importMode === 'replace' && importPreview.controlMappings && !importPreview.controlMappings.every((m) => !!controlMappings[m.roleKey]) && (
                      <span className="text-[11px] text-amber-600 dark:text-amber-400 font-medium">
                        Remap all control roles to proceed
                      </span>
                    )}
                    <button
                      type="button"
                      disabled={
                        importPreview.hasErrors ||
                        !importPreview.canCommit ||
                        committingImport ||
                        (importMode === 'replace' && importPreview.controlMappings && !importPreview.controlMappings.every((m) => !!controlMappings[m.roleKey]))
                      }
                      onClick={handleCommitImport}
                      className="h-8 px-4 rounded-lg bg-[var(--brand-primary)] font-semibold text-[var(--brand-text)] hover:opacity-90 disabled:opacity-50 text-xs flex items-center gap-1.5"
                    >
                      {committingImport && <Loader2 className="w-3.5 h-3.5 animate-spin" />}
                      Confirm & Import
                    </button>
                  </div>
                </>
              )}

              {importStep === 'success' && (
                <div className="w-full flex justify-end">
                  <button
                    type="button"
                    onClick={() => setImportModalOpen(false)}
                    className="h-8 px-4 rounded-lg bg-[var(--brand-primary)] font-semibold text-[var(--brand-text)] hover:opacity-90 text-xs"
                  >
                    Done & View Accounts
                  </button>
                </div>
              )}
            </div>
          </div>
      </ModalShell>
    </div>
  );
};

const Badge: React.FC<{
  muted?: boolean;
  variant?: 'brand' | 'archived' | 'control';
  children: React.ReactNode;
}> = ({ muted, variant = 'brand', children }) => {
  let styleClass = 'bg-[var(--brand-subtle)] text-[var(--brand-primary)]';
  if (muted) {
    styleClass = 'bg-[var(--bg-subtle)] text-[var(--text-muted)]';
  } else if (variant === 'archived') {
    styleClass = 'bg-gray-200 dark:bg-gray-800 text-gray-600 dark:text-gray-400';
  } else if (variant === 'control') {
    styleClass = 'bg-indigo-100 dark:bg-indigo-950/60 text-indigo-700 dark:text-indigo-300 ring-1 ring-indigo-300 dark:ring-indigo-800';
  }

  return (
    <span className={`shrink-0 px-1.5 py-0.5 rounded text-[9px] font-bold uppercase tracking-wide ${styleClass}`}>
      {children}
    </span>
  );
};

export default ChartOfAccounts;
