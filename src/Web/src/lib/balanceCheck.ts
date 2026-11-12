/** Parses what the user typed into the balance box; null when it is not a usable number. */
export function parseBalanceInput(raw: string): number | null {
  const cleaned = raw.trim().replace(/[,\s]/g, '');
  if (!cleaned) return null;
  const parenthesised = /^\((.*)\)$/.exec(cleaned);
  const numeric = Number(parenthesised ? `-${parenthesised[1]}` : cleaned);
  return Number.isFinite(numeric) ? numeric : null;
}

/** The balance check is for Invoicing & Bills workspaces; Accounting reconciles instead. */
export function canShowBalanceCheck(options: {
  hasFinancials: boolean;
  canView: boolean;
  archived: boolean;
}): boolean {
  return !options.hasFinancials && options.canView && !options.archived;
}

/**
 * The date the balance dialog opens with. A balance handed over from a statement keeps the statement's date, or
 * stays empty when the file gave none (never today). A manual check starts at today.
 */
export function initialBalanceCheckDate(
  initialBalance: number | string | null | undefined,
  initialAsOfDate: string | null | undefined,
  today: string,
): string {
  if (initialAsOfDate) return String(initialAsOfDate).slice(0, 10);
  return initialBalance != null ? '' : today;
}
