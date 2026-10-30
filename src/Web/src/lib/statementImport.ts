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

/** Same rule as the API: comma, semicolon, or tab — whichever appears most in the first non-blank line. */
export function detectDelimiter(content: string): string {
  const firstLine =
    content
      .replace(/^\uFEFF/, '')
      .split(/\r\n|\r|\n/)
      .find((line) => line.trim().length > 0) ?? '';
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

export function sourceForFileName(fileName: string): 1 | 2 | 4 | 5 | 6 | 7 | null {
  const lower = fileName.toLowerCase();
  if (lower.endsWith('.csv') || lower.endsWith('.txt')) return 1;
  if (lower.endsWith('.ofx') || lower.endsWith('.qfx')) return 2;
  if (lower.endsWith('.xml') || lower.endsWith('.camt')) return 4;
  if (lower.endsWith('.sta') || lower.endsWith('.mt940') || lower.endsWith('.swi')) return 5;
  if (lower.endsWith('.qif')) return 6;
  if (lower.endsWith('.xlsx')) return 7;
  return null;
}

export function emptyColumnMap(): BankStatementColumnMap {
  return {
    hasHeaderRow: true,
    headerRowIndex: null,
    delimiter: null,
    dateFormat: null,
    dateColumn: 0,
    amountColumn: null,
    depositColumn: null,
    withdrawalColumn: null,
    directionColumn: null,
    payeeColumn: null,
    memoColumn: null,
    referenceColumn: null,
    externalIdColumn: null,
    currencyColumn: null,
    statusColumn: null,
    allowedStatuses: null,
    invertSign: false,
  };
}

/** Decode statement bytes: BOM (UTF-8 / UTF-16 LE/BE), else UTF-8, else Windows-1252. Mirrors StatementFileEncoding. */
export function decodeStatementBytes(bytes: Uint8Array): string {
  if (bytes.length >= 3 && bytes[0] === 0xef && bytes[1] === 0xbb && bytes[2] === 0xbf) {
    return new TextDecoder('utf-8').decode(bytes.subarray(3));
  }
  if (bytes.length >= 2 && bytes[0] === 0xff && bytes[1] === 0xfe) {
    return new TextDecoder('utf-16le').decode(bytes.subarray(2));
  }
  if (bytes.length >= 2 && bytes[0] === 0xfe && bytes[1] === 0xff) {
    return new TextDecoder('utf-16be').decode(bytes.subarray(2));
  }
  if (isValidUtf8(bytes)) {
    return new TextDecoder('utf-8').decode(bytes);
  }
  // Windows-1252 best-effort via latin1 code points 0x80–0x9F mapped roughly; browsers lack 1252.
  // Prefer sending contentBase64 so the API does the authoritative decode.
  return new TextDecoder('latin1').decode(bytes);
}

function isValidUtf8(bytes: Uint8Array): boolean {
  let i = 0;
  while (i < bytes.length) {
    const b = bytes[i];
    if (b <= 0x7f) {
      i += 1;
      continue;
    }
    let need = 0;
    if ((b & 0xe0) === 0xc0) need = 1;
    else if ((b & 0xf0) === 0xe0) need = 2;
    else if ((b & 0xf8) === 0xf0) need = 3;
    else return false;
    if (i + need >= bytes.length) return false;
    if (need === 1 && b < 0xc2) return false;
    for (let j = 1; j <= need; j += 1) {
      if ((bytes[i + j] & 0xc0) !== 0x80) return false;
    }
    i += 1 + need;
  }
  return true;
}

/** Base64 of the raw file bytes for API encoding detection. */
export function bytesToBase64(bytes: Uint8Array): string {
  let binary = '';
  const chunk = 0x8000;
  for (let i = 0; i < bytes.length; i += chunk) {
    binary += String.fromCharCode(...bytes.subarray(i, i + chunk));
  }
  return btoa(binary);
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
  map.dateColumn =
    findHeader(headers, [/^created_on$/i, /^(posted\s*)?date$/i, /^finished_on$/i, /date/i]) ?? 0;
  map.amountColumn = findHeader(headers, [/^signed_amount$/i, /^amount$/i, /^(transaction\s*)?amount/i]);
  if (map.amountColumn == null) {
    map.depositColumn = findHeader(headers, [/deposit/i, /credit/i, /money\s*in/i]);
    map.withdrawalColumn = findHeader(headers, [/withdraw/i, /debit/i, /money\s*out/i, /payment/i]);
  }
  map.directionColumn = findHeader(headers, [/^direction$/i, /^type$/i]);
  map.payeeColumn = findHeader(headers, [/^payee$/i, /^source_name$/i, /^name$/i, /description/i, /merchant/i]);
  map.memoColumn = findHeader(headers, [/^memo$/i, /^notes?$/i, /details?/i, /^target_name$/i]);
  map.referenceColumn = findHeader(headers, [/^ref/i, /check/i, /cheque/i]);
  map.externalIdColumn = findHeader(headers, [/activity_id/i, /fitid/i, /transaction\s*id/i, /^id$/i]);
  map.currencyColumn = findHeader(headers, [/^currency$/i, /^ccy$/i]);
  map.statusColumn = findHeader(headers, [/^status$/i, /^state$/i]);
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

/**
 * After matching or creating the last line on a page, totalCount can drop so the
 * requested page is past the end. Clamp to the last page that still has rows (or 1).
 */
export function clampStatementLinePage(page: number, totalCount: number, pageSize: number): number {
  const size = Math.max(1, pageSize);
  const requested = Number.isFinite(page) && page >= 1 ? Math.floor(page) : 1;
  const lastPage = Math.max(1, Math.ceil(Math.max(0, totalCount) / size));
  return Math.min(requested, lastPage);
}

/** Normalize bank payee / vendor name for matching (#436 I2). */
export function normalizePayeeName(value: string | null | undefined): string {
  return (value ?? '')
    .trim()
    .toLowerCase()
    .replace(/\s+/g, ' ');
}

/** Prefer an exact vendor name match for the bank description; otherwise null. */
export function matchVendorIdByPayee(
  payee: string | null | undefined,
  vendors: { id: number; name?: string | null }[],
): number | null {
  const needle = normalizePayeeName(payee);
  if (!needle) return null;
  const exact = vendors.find((v) => normalizePayeeName(v.name) === needle);
  return exact?.id ?? null;
}
