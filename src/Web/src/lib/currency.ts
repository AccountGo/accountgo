export const DEFAULT_CURRENCY = 'USD';

// Display only: amounts are never converted when the workspace currency changes.
const SYMBOLS: Record<string, string> = {
  USD: '$',
  PHP: '₱',
  CAD: 'CA$',
  EUR: '€',
  GBP: '£',
  AUD: 'AU$',
  SGD: 'S$',
};

export const currencySymbol = (code?: string | null): string =>
  SYMBOLS[(code || DEFAULT_CURRENCY).toUpperCase()] ?? '$';
