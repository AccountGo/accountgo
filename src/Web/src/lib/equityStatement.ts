export interface EquityColumn { key: string; label: string }
export interface EquityRow {
  key: string;
  label: string;
  amounts: Record<string, number>;
  total: number;
}
export interface EquityStatement {
  entityType: string;
  from: string;
  to: string;
  basis: string;
  partnerMode?: boolean;
  hasUnallocatedProfit?: boolean;
  columns: EquityColumn[];
  rows: EquityRow[];
  balanceSheetEquity: number;
  differenceToBalanceSheet: number;
  comparative?: EquityStatement | null;
}

export const showEquityStatement = (personal: boolean): boolean => !personal;
export function equityTableColumns(statement: Pick<EquityStatement, 'columns'>): EquityColumn[] {
  return statement.columns.length > 1 ? [...statement.columns, { key: 'total', label: 'Total' }] : statement.columns;
}
export function equityTableRows(statement: Pick<EquityStatement, 'rows'>): EquityRow[] {
  return statement.rows.filter(row => ['opening', 'profit', 'closing', 'restatedOpening'].includes(row.key)
    || Object.values(row.amounts).some(value => value !== 0));
}
export function equityReconciliation(statement: Pick<EquityStatement, 'columns' | 'rows' | 'differenceToBalanceSheet' | 'balanceSheetEquity'>,
  format: (value: number) => string): { agrees: boolean; message: string } {
  if (Math.abs(statement.differenceToBalanceSheet) >= 0.005) return {
    agrees: false,
    message: `Doesn't agree with the balance sheet by ${format(statement.differenceToBalanceSheet)}. Check equity accounts posted outside the usual codes.`,
  };
  for (const column of statement.columns) {
    const closing = statement.rows.find(row => row.key === 'closing')?.amounts[column.key] ?? 0;
    const bridge = statement.rows.filter(row => row.key !== 'closing' && row.key !== 'restatedOpening')
      .reduce((sum, row) => sum + (row.amounts[column.key] ?? 0), 0);
    if (Math.abs(closing - bridge) >= 0.005) return {
      agrees: false, message: `${column.label} movements don't reconcile to closing by ${format(closing - bridge)}.`,
    };
  }
  return { agrees: true, message: `Agrees with the balance sheet (equity ${format(statement.balanceSheetEquity)})` };
}

/** Export both periods with component columns, preserving signed currency units. */
export function equityStatementCsv(statement: EquityStatement): string {
  const cells: string[][] = [];
  for (const [index, report] of [statement, statement.comparative].entries()) {
    if (!report) continue;
    if (index) cells.push([], [`Previous period: ${report.from.slice(0, 10)} – ${report.to.slice(0, 10)}`]);
    const columns = equityTableColumns(report);
    cells.push(['Period', report.from.slice(0, 10), report.to.slice(0, 10)], ['Movement', ...columns.map(c => c.label)]);
    for (const row of equityTableRows(report))
      cells.push([row.label, ...columns.map(c => (c.key === 'total' ? row.total : row.amounts[c.key] ?? 0).toFixed(2))]);
    cells.push([equityReconciliation(report, n => n.toFixed(2)).message]);
    if (report.partnerMode && report.hasUnallocatedProfit)
      cells.push(['Profit is allocated to partners when the fiscal year is closed.']);
  }
  return cells.map(row => row.map(cell => {
    const safe = /^\s*[=+@\t\r-]/.test(cell) && !/^-?\d+(\.\d+)?$/.test(cell) ? `'${cell}` : cell;
    return `"${safe.replaceAll('"', '""')}"`;
  }).join(',')).join('\r\n');
}
