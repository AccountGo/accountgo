import type { BankAccount, OutstandingAging } from '../types/banking';

export interface AgingRow {
  account: BankAccount;
  aging: OutstandingAging;
}

/** Only accounts with something outstanding are worth a row. */
export function rowsWithOutstanding(rows: AgingRow[]): AgingRow[] {
  return rows.filter((r) => r.aging.totalCount > 0);
}

export function staleTotal(rows: AgingRow[]): number {
  return rows.reduce((sum, r) => sum + r.aging.staleCount, 0);
}

export function staleMessage(count: number): string {
  if (count <= 0) return '';
  return `${count} item${count === 1 ? ' is' : 's are'} outstanding for more than 90 days. Check the statement or void them.`;
}
