import type { BankCloseStatus } from '../types/banking';

export function closeStatusMessage(status: BankCloseStatus): string {
  return `Bank accounts reconciled through ${status.periodLabel}: ${status.reconciledCount} of ${status.totalCount}`;
}

/** Nothing to show without active bank accounts. */
export function shouldShowCloseStatus(status: BankCloseStatus | null | undefined): status is BankCloseStatus {
  return !!status && status.totalCount > 0;
}

export function unreconciledBanks(status: BankCloseStatus) {
  return status.banks.filter((bank) => !bank.isReconciled);
}
