/** Why a statement's closing balance was not offered (or has no date). Codes come from the API. */
const NOTICE_COPY: Record<string, string> = {
  not_booked:
    "This file only has an available or interim balance, not a closing balance, so it wasn't used. You can still check the balance by hand.",
  other_account:
    "This file looks like it is for a different account, so its closing balance wasn't used. You can still check the balance by hand.",
  multiple_accounts:
    "This file has statements for more than one account, so its closing balance wasn't used. You can still check the balance by hand.",
  currency_mismatch:
    "The closing balance in this file is in a different currency from this account, so it wasn't used. You can still check the balance by hand.",
  currency_missing:
    "This file doesn't say which currency its closing balance is in, so it wasn't used. You can still check the balance by hand.",
  ambiguous_latest:
    "This file has more than one closing balance and we couldn't tell which is the latest, so none was used. You can still check the balance by hand.",
  date_missing: 'This file gives no date for its closing balance. Enter the date shown on your statement.',
};

/** User-facing text for a notice code; null for no or unknown code. */
export function statementBalanceNotice(code?: string | null): string | null {
  if (!code) return null;
  return NOTICE_COPY[code] ?? null;
}

export interface StatementBalanceOffer {
  balance: number;
  asOfDate?: string | null;
  currency?: string | null;
  /** Last four characters of the account the balance belongs to. */
  account?: string | null;
}

/** The banner sentence: signed amount, currency, date (or a note that the file has none) and the account suffix. */
export function formatStatementBalanceBanner(
  offer: StatementBalanceOffer,
  formatAmount: (n: number, currency?: string | null) => string,
): string {
  const amount = formatAmount(offer.balance, offer.currency);
  const overdrawn = offer.balance < 0 ? ' (overdrawn)' : '';
  const date = offer.asOfDate ? `as of ${String(offer.asOfDate).slice(0, 10)}` : 'no date in the file';
  const account = offer.account ? ` for account ending ${offer.account}` : '';
  return `Statement closing balance: ${amount}${overdrawn}, ${date}${account}.`;
}
