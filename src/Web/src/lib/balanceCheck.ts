import type { BankBalanceCheck } from '../types/banking';

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

/**
 * Formats the balance check result message with the localized currency.
 * Replaces any unformatted amount in the server message with the formatted value,
 * e.g. "1234.56" becomes "$1,234.56".
 */
export function formatBalanceCheckMessage(
  result: Pick<BankBalanceCheck, 'isMatch' | 'difference' | 'message'>,
  formatCurrency: (n: number) => string,
): string {
  if (result.isMatch || result.difference === 0) {
    return result.message || 'Your books match the bank.';
  }
  const rawAmount = Math.abs(result.difference).toFixed(2);
  const formatted = formatCurrency(Math.abs(result.difference));
  if (result.message && result.message.includes(rawAmount)) {
    return result.message.replace(rawAmount, formatted);
  }
  return result.difference > 0
    ? `The bank shows ${formatted} more than your books. Look for a deposit you have not recorded or a payment recorded twice.`
    : `The bank shows ${formatted} less than your books. Look for a payment you have not recorded or a deposit that has not arrived yet.`;
}
