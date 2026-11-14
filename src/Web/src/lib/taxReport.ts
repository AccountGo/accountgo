import { toCsv } from './csv';

export type TaxPeriodFrequency = 'Monthly' | 'Quarterly';

export interface TaxPeriodSettings {
  frequency: TaxPeriodFrequency;
  quarterCycleStartMonth: number | null;
  effectiveQuarterCycleStartMonth: number;
  defaultQuarterCycleStartMonth: number;
}

export interface TaxPeriodItem {
  start: string;
  end: string;
  label: string;
  state: 'open' | 'ended';
  lock: 'none' | 'partial' | 'full' | null;
  filing: null;
}

export interface TaxPeriodTaxRow {
  taxId: number | null;
  taxCode: string;
  taxName: string;
  rate: number | null;
  taxable: number;
  tax: number;
  documentCount: number;
}

export interface TaxPeriodSideSummary {
  rows: TaxPeriodTaxRow[];
  taxable: number;
  tax: number;
}

export interface TaxPeriodDocumentRow {
  side: 'output' | 'input';
  kind: 'invoice' | 'invoiceVoid' | 'bill' | 'billVoid';
  id: number;
  no: string;
  date: string;
  party: string;
  taxId: number | null;
  taxable: number;
  tax: number;
  posted: boolean;
}

export interface TaxPeriodUnpostedWarning {
  kind: 'invoice' | 'bill';
  id: number;
  no: string;
  date: string;
}

export interface TaxPeriodLedgerOtherEntry {
  glHeaderId: number;
  date: string;
  description: string;
  documentType: string;
  amount: number;
}

export interface TaxPeriodLedgerAccount {
  accountId: number;
  code: string;
  name: string;
  positiveSide: 'credit' | 'debit';
  movement: number;
  fromDocuments: number;
  other: number;
  entries: TaxPeriodLedgerOtherEntry[];
}

export interface TaxPeriodLockStatus {
  lockedThrough: string | null;
  state: 'none' | 'partial' | 'full';
  canLock: boolean;
}

export interface TaxPeriodReport {
  from: string;
  to: string;
  basis: string;
  currency: string;
  isWholePeriod: boolean;
  periodLabel: string | null;
  output: TaxPeriodSideSummary;
  input: TaxPeriodSideSummary;
  netTax: number;
  documents: TaxPeriodDocumentRow[];
  documentsTruncated: boolean;
  warnings: {
    unposted: TaxPeriodUnpostedWarning[];
  };
  ledger: {
    accounts: TaxPeriodLedgerAccount[];
  } | null;
  lock: TaxPeriodLockStatus | null;
}

export interface TaxNavVisibilityInput {
  isPersonalBooks?: boolean;
  usesTax?: boolean;
  activeModules?: string[];
  taxRegime?: 'None' | 'SalesTax' | 'VatGst';
  hasPositiveTaxRate?: boolean;
}

export function canShowTaxNav(input: TaxNavVisibilityInput): boolean {
  if (input.isPersonalBooks) return false;
  const modules = input.activeModules ?? [];
  const hasRelevantModule =
    modules.includes('sales') || modules.includes('purchases') || modules.includes('financials');
  if (!hasRelevantModule) return false;
  if (typeof input.usesTax === 'boolean') return input.usesTax;
  return (input.taxRegime != null && input.taxRegime !== 'None') || Boolean(input.hasPositiveTaxRate);
}

const ISO_DATE_RE = /^\d{4}-\d{2}-\d{2}$/;

export function isValidTaxDateRange(from: string, to: string): boolean {
  if (!ISO_DATE_RE.test(from) || !ISO_DATE_RE.test(to)) return false;
  const [fy, fm, fd] = from.split('-').map(Number);
  const [ty, tm, td] = to.split('-').map(Number);
  const fDate = new Date(Date.UTC(fy, fm - 1, fd));
  const tDate = new Date(Date.UTC(ty, tm - 1, td));
  if (
    fDate.getUTCFullYear() !== fy ||
    fDate.getUTCMonth() !== fm - 1 ||
    fDate.getUTCDate() !== fd ||
    tDate.getUTCFullYear() !== ty ||
    tDate.getUTCMonth() !== tm - 1 ||
    tDate.getUTCDate() !== td
  ) {
    return false;
  }
  return from <= to;
}

export function createLatestRequestGuard() {
  let seq = 0;
  let activeKey = '';
  return {
    next(key: string): number {
      seq += 1;
      activeKey = key;
      return seq;
    },
    isCurrent(id: number, key: string): boolean {
      return id === seq && key === activeKey;
    },
    invalidate(): void {
      seq += 1;
      activeKey = '';
    },
  };
}

export function buildTaxReportCsv(
  report: TaxPeriodReport,
  workspaceName: string,
  generatedAtIso: string,
): string {
  const money = (value: number) => value.toFixed(2);
  const periodText = report.periodLabel ? report.periodLabel : `${report.from} to ${report.to}`;
  const netLabel = report.netTax < 0 ? 'Net refundable' : 'Net payable';
  const netValue = money(Math.abs(report.netTax));
  const basisLabel =
    report.basis === 'Accrual' ? 'Accrual (by invoice and bill date)' : report.basis;

  const taxCodeById = new Map<number, string>();
  for (const row of report.output.rows) {
    if (row.taxId !== null) taxCodeById.set(row.taxId, row.taxCode);
  }
  for (const row of report.input.rows) {
    if (row.taxId !== null && !taxCodeById.has(row.taxId)) {
      taxCodeById.set(row.taxId, row.taxCode);
    }
  }

  const sideRows = (title: string, side: TaxPeriodSideSummary): string[][] => [
    [title],
    ['Code', 'Name', 'Rate', 'Taxable amount', 'Tax'],
    ...side.rows.map((r) => [
      r.taxCode,
      r.taxName,
      r.rate === null ? '—' : `${r.rate}%`,
      money(r.taxable),
      money(r.tax),
    ]),
    ['Total', '', '', money(side.taxable), money(side.tax)],
  ];

  const rows: string[][] = [
    ['Workspace', workspaceName],
    ['Period', periodText],
    ['Basis', basisLabel],
    ['Currency', report.currency],
    ['Generated at', generatedAtIso],
    [],
    ...sideRows('Sales', report.output),
    [],
    ...sideRows('Purchases', report.input),
    [],
    [netLabel, netValue],
    [],
    ['Documents'],
    ['Side', 'Kind', 'No', 'Date', 'Party', 'Tax code', 'Taxable', 'Tax', 'Posted'],
    ...report.documents.map((d) => [
      d.side,
      d.kind,
      d.no,
      d.date,
      d.party,
      d.taxId === null ? 'No tax' : (taxCodeById.get(d.taxId) ?? 'No tax'),
      money(d.taxable),
      money(d.tax),
      d.posted ? 'Yes' : 'No',
    ]),
  ];

  return toCsv(rows);
}
