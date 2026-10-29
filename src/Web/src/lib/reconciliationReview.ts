import type { BankReconciliationListItem } from '../types/banking';

// Mirrors BANK_RECONCILIATION_STATUS.Finished; kept local so this module stays free of runtime imports.
const FINISHED_STATUS = 2;

function sameIdentity(a?: string | null, b?: string | null): boolean {
  return !!a && !!b && a.trim().toLowerCase() === b.trim().toLowerCase();
}

/**
 * Courtesy check so the button is hidden for the preparer; the API enforces the real rule
 * (reviewer must differ from FinishedBy, finished only, not already reviewed).
 */
export function canReviewReconciliation(
  item: Pick<BankReconciliationListItem, 'status' | 'finishedBy' | 'reviewedAt'>,
  currentUser: string | null | undefined,
  hasReconcilePermission: boolean,
): boolean {
  if (!hasReconcilePermission) return false;
  if (item.status !== FINISHED_STATUS) return false;
  if (item.reviewedAt) return false;
  return !sameIdentity(item.finishedBy, currentUser);
}

export function reviewStatusText(item: Pick<BankReconciliationListItem, 'reviewedAt' | 'reviewedBy'>): string {
  if (!item.reviewedAt) return 'Not reviewed';
  const day = String(item.reviewedAt).slice(0, 10);
  return item.reviewedBy ? `${day} · ${item.reviewedBy}` : day;
}
