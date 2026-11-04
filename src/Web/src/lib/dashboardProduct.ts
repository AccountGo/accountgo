import type { OrgDetails } from '../types';

export interface DashboardCapabilities {
  financials?: boolean;
  sales?: boolean;
  purchases?: boolean;
  banking?: boolean;
  moneyInBank?: boolean;
  receivables?: boolean;
  payables?: boolean;
}

/** Prefer the server-authorized surface; fall back only when the summary has not loaded yet. */
export function resolveDashboardSurface(
  serverSurface: string | null | undefined,
  hasFinancialsModule: boolean,
): 'Accounting' | 'Invoicing' {
  if (serverSurface === 'Accounting' || serverSurface === 'Invoicing') return serverSurface;
  return hasFinancialsModule ? 'Accounting' : 'Invoicing';
}

export function isPersonalWorkspace(org: OrgDetails | null | undefined): boolean {
  return org?.operatingPolicy?.purpose === 'Personal';
}

/**
 * Recorded cash + open receivables − open bills. Not a cash forecast and not available cash.
 * Returns null unless every input was authorized and loaded.
 */
export function recordedCashReceivablesMinusBills(input: {
  moneyInBank: number | null | undefined;
  outstandingReceivables: number | null | undefined;
  totalPayables: number | null | undefined;
  moneyInBankAvailable: boolean;
  receivablesAvailable: boolean;
  payablesAvailable: boolean;
}): number | null {
  if (!input.moneyInBankAvailable || !input.receivablesAvailable || !input.payablesAvailable) return null;
  if (input.moneyInBank == null || input.outstandingReceivables == null || input.totalPayables == null) return null;
  return input.moneyInBank + input.outstandingReceivables - input.totalPayables;
}
