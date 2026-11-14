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

export function buildTaxReportCsv(
  report: TaxPeriodReport,
  workspaceName: string,
  generatedAtIso: string,
): string {
  const money = (value: number) => value.toFixed(2);
  const periodText = report.periodLabel ? report.periodLabel : `${report.from} to ${report.to}`;
  const netLabel = report.netTax < 0 ? 'Net refundable' : 'Net payable';
  const netValue = money(Math.abs(report.netTax));

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
    ['Basis', report.basis],
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
    ['Side', 'Kind', 'No', 'Date', 'Party', 'Tax ID', 'Taxable', 'Tax', 'Posted'],
    ...report.documents.map((d) => [
      d.side,
      d.kind,
      d.no,
      d.date,
      d.party,
      d.taxId === null ? '' : String(d.taxId),
      money(d.taxable),
      money(d.tax),
      d.posted ? 'Yes' : 'No',
    ]),
  ];

  return toCsv(rows);
}
