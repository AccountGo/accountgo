export interface DashboardBankBalance {
  bankId: number;
  name: string;
  balance: number;
  /** An archived bank that still holds a balance. */
  isArchived?: boolean;
}

export interface MoneyInBankKpis {
  moneyInBank: number;
  bankBalances?: DashboardBankBalance[] | null;
  unassignedCash?: number | null;
}

export interface MoneyInBankRow {
  key: string;
  label: string;
  balance: number;
  /** Register for one bank; the banking list for cash not tied to a bank. */
  path: string;
}

const CENTS_PER_UNIT = 100;

/** Bank register path for the workspace; the list page when no bank is given (unassigned cash). */
export function bankRegisterPath(orgSlug: string, bankId?: number): string {
  return bankId === undefined ? `/org/${orgSlug}/banking` : `/org/${orgSlug}/banking/${bankId}`;
}

/**
 * Rows for the money-in-bank breakdown: one per active bank, archived banks that still hold a balance (labelled),
 * then unassigned cash when there is any.
 * Empty when the caller cannot see bank accounts or there is nothing to split.
 */
export function moneyInBankRows(kpis: MoneyInBankKpis | undefined | null, orgSlug: string): MoneyInBankRow[] {
  if (!kpis || !kpis.bankBalances) return [];

  const rows: MoneyInBankRow[] = kpis.bankBalances.map((bank) => ({
    key: `bank-${bank.bankId}`,
    label: bank.isArchived ? `${bank.name} (archived)` : bank.name,
    balance: bank.balance,
    path: bankRegisterPath(orgSlug, bank.bankId),
  }));

  const unassigned = kpis.unassignedCash ?? 0;
  if (Math.round(unassigned * CENTS_PER_UNIT) !== 0) {
    rows.push({
      key: 'unassigned',
      label: 'Cash not assigned to a bank account',
      balance: unassigned,
      path: bankRegisterPath(orgSlug),
    });
  }
  return rows;
}

/** True when the rows add up to the headline total (to the cent). */
export function rowsMatchTotal(rows: MoneyInBankRow[], total: number): boolean {
  const cents = rows.reduce((sum, row) => sum + Math.round(row.balance * CENTS_PER_UNIT), 0);
  return cents === Math.round(total * CENTS_PER_UNIT);
}
