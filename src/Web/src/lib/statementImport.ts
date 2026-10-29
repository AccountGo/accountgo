/** Pure helpers for the bank statement import page (CSV header sniffing, column suggestions, kinds). */

import type { BankStatementColumnMap } from '../types/banking';

export const CSV_SAMPLE_ROWS = 5;

/** Split one CSV text into rows of fields (RFC 4180 quoting). Reads at most `maxRows` rows. */
export function parseCsvRows(content: string, delimiter: string, maxRows = CSV_SAMPLE_ROWS + 1): string[][] {
  const text = content.replace(/^\uFEFF/, '');
  const rows: string[][] = [];
  let field = '';
  let fields: string[] = [];
  let inQuotes = false;

  for (let i = 0; i < text.length && rows.length < maxRows; i += 1) {
    const ch = text[i];
    if (inQuotes) {
      if (ch === '"') {
        if (text[i + 1] === '"') {
          field += '"';
          i += 1;
        } else {
          inQuotes = false;
        }
      } else {
        field += ch;
      }
      continue;
    }
    if (ch === '"' && field.length === 0) {
      inQuotes = true;
    } else if (ch === delimiter) {
      fields.push(field);
      field = '';
    } else if (ch === '\r' || ch === '\n') {
      if (ch === '\r' && text[i + 1] === '\n') i += 1;
      fields.push(field);
      field = '';
      if (fields.some((f) => f.trim() !== '')) rows.push(fields);
      fields = [];
    } else {
      field += ch;
    }
  }
  if (rows.length < maxRows && (field.length > 0 || fields.length > 0)) {
    fields.push(field);
    if (fields.some((f) => f.trim() !== '')) rows.push(fields);
  }
  return rows;
}

/** Same rule as the API: comma, semicolon, or tab — whichever appears most in the first line. */
export function detectDelimiter(content: string): string {
  const firstLine = content.replace(/^\uFEFF/, '').split(/\r\n|\r|\n/, 1)[0] ?? '';
  let best = ',';
  let bestCount = 0;
  for (const candidate of [',', ';', '\t']) {
    let count = 0;
    let inQuotes = false;
    for (const ch of firstLine) {
      if (ch === '"') inQuotes = !inQuotes;
      else if (ch === candidate && !inQuotes) count += 1;
    }
    if (count > bestCount) {
      best = candidate;
      bestCount = count;
    }
  }
  return best;
}

export function sourceForFileName(fileName: string): 1 | 2 | null {
  const lower = fileName.toLowerCase();
  if (lower.endsWith('.csv') || lower.endsWith('.txt')) return 1;
  if (lower.endsWith('.ofx') || lower.endsWith('.qfx')) return 2;
  return null;
}

export function emptyColumnMap(): BankStatementColumnMap {
  return {
    hasHeaderRow: true,
    delimiter: null,
    dateFormat: null,
    dateColumn: 0,
    amountColumn: null,
    depositColumn: null,
    withdrawalColumn: null,
    payeeColumn: null,
    memoColumn: null,
    referenceColumn: null,
    externalIdColumn: null,
    invertSign: false,
  };
}

function findHeader(headers: string[], patterns: RegExp[]): number | null {
  for (const pattern of patterns) {
    const index = headers.findIndex((h) => pattern.test(h.trim()));
    if (index >= 0) return index;
  }
  return null;
}

/** Guess a column map from header names; the user can change every choice. */
export function suggestColumnMap(headers: string[]): BankStatementColumnMap {
  const map = emptyColumnMap();
  map.dateColumn = findHeader(headers, [/^(posted\s*)?date$/i, /date/i]) ?? 0;
  map.amountColumn = findHeader(headers, [/^amount$/i, /^(transaction\s*)?amount/i]);
  if (map.amountColumn == null) {
    map.depositColumn = findHeader(headers, [/deposit/i, /credit/i, /money\s*in/i]);
    map.withdrawalColumn = findHeader(headers, [/withdraw/i, /debit/i, /money\s*out/i, /payment/i]);
  }
  map.payeeColumn = findHeader(headers, [/^payee$/i, /^name$/i, /description/i, /merchant/i]);
  map.memoColumn = findHeader(headers, [/^memo$/i, /^notes?$/i, /details?/i]);
  map.referenceColumn = findHeader(headers, [/^ref/i, /check/i, /cheque/i]);
  map.externalIdColumn = findHeader(headers, [/fitid/i, /transaction\s*id/i, /^id$/i]);
  return map;
}

export function hasAmountMapping(map: BankStatementColumnMap): boolean {
  return map.amountColumn != null || map.depositColumn != null || map.withdrawalColumn != null;
}

export const KIND_MONEY_IN = 1;
export const KIND_MONEY_OUT = 2;
export const KIND_FEE = 4;
export const KIND_INTEREST = 5;
export const KIND_ADJUSTMENT = 6;

/** Transaction kinds that make sense for a statement line: deposits vs payments. */
export function kindsForAmount(amount: number): { value: number; label: string; needsContra: boolean }[] {
  if (amount > 0) {
    return [
      { value: KIND_MONEY_IN, label: 'Money in', needsContra: true },
      { value: KIND_INTEREST, label: 'Interest', needsContra: false },
      { value: KIND_ADJUSTMENT, label: 'Adjustment', needsContra: true },
    ];
  }
  return [
    { value: KIND_MONEY_OUT, label: 'Money out', needsContra: true },
    { value: KIND_FEE, label: 'Fee', needsContra: false },
    { value: KIND_ADJUSTMENT, label: 'Adjustment', needsContra: true },
  ];
}

export function importSummary(result: {
  imported: number;
  duplicates: number;
  errors: number;
  autoMatched: number;
}): string {
  const parts = [`Imported ${result.imported} line${result.imported === 1 ? '' : 's'}`];
  if (result.autoMatched > 0) parts.push(`${result.autoMatched} matched automatically`);
  if (result.duplicates > 0) parts.push(`${result.duplicates} already imported`);
  if (result.errors > 0) parts.push(`${result.errors} skipped with errors`);
  return `${parts.join(', ')}.`;
}
