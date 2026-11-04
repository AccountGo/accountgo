import type { OrgDetails } from '../types';

/** True when the workspace cannot see the ledger — invoicing product surface. */
export function isInvoicingDashboard(hasFinancialsModule: boolean): boolean {
  return !hasFinancialsModule;
}

export function isPersonalWorkspace(org: OrgDetails | null | undefined): boolean {
  return org?.operatingPolicy?.purpose === 'Personal';
}

/** Near-term cash picture available without a full balance sheet. */
export function nearTermCashPosition(input: {
  moneyInBank?: number;
  outstandingReceivables?: number;
  totalPayables?: number;
}): number {
  return (input.moneyInBank ?? 0) + (input.outstandingReceivables ?? 0) - (input.totalPayables ?? 0);
}
