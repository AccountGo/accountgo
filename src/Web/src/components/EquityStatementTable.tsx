import { equityReconciliation, equityTableColumns, equityTableRows } from '../lib/equityStatement';
import type { EquityStatement } from '../lib/equityStatement';

export function EquityStatementTable({ statement, formatAmount }: { statement: EquityStatement; formatAmount: (n: number) => string }) {
  const columns = equityTableColumns(statement);
  const reconciliation = equityReconciliation(statement, formatAmount);
  return <>
    <div className="overflow-x-auto">
      <table className="w-full text-sm">
        <caption className="sr-only">Changes in Equity, {statement.from.slice(0, 10)} to {statement.to.slice(0, 10)}</caption>
        <thead className="bg-[var(--bg-subtle)] text-[var(--text-secondary)]"><tr>
          <th scope="col" className="p-4 text-left">Movement</th>
          {columns.map(column => <th scope="col" key={column.key} className="p-4 text-right">{column.label}</th>)}
        </tr></thead>
        <tbody>{equityTableRows(statement).map(row => <tr key={row.key}
          className={`border-t border-[var(--border-subtle)] ${['opening', 'restatedOpening', 'closing'].includes(row.key) ? 'font-semibold bg-[var(--bg-subtle)]' : ''}`}>
          <th scope="row" className="p-4 text-left font-[inherit]">{row.label}</th>
          {columns.map(column => <td key={column.key} className="p-4 text-right tabular-nums">
            {formatAmount(column.key === 'total' ? row.total : row.amounts[column.key] ?? 0)}
          </td>)}
        </tr>)}</tbody>
      </table>
    </div>
    <p role={reconciliation.agrees ? 'status' : 'alert'} className={`p-5 text-sm ${reconciliation.agrees ? 'text-emerald-500' : 'text-red-500'}`}>
      {reconciliation.message}
    </p>
    {statement.partnerMode && statement.hasUnallocatedProfit &&
      <p className="px-5 pb-4 text-sm text-[var(--text-muted)]">Profit is allocated to partners when the fiscal year is closed.</p>}
  </>;
}
