import type { DateRange, FinancialYear } from '../pages/Reports';

export type ComparativePreset = 'month' | 'quarter' | 'year' | 'all' | 'custom';
export type ComparativeStatement = 'income' | 'balanceSheet';

function parseYmd(value: string): Date {
  const [y, m, d] = value.slice(0, 10).split('-').map(Number);
  return new Date(y, m - 1, d);
}

function formatYmd(date: Date): string {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}

/** Shift by whole years, clamping the day to the target month's last day (2028-02-29 → 2027-02-28). */
export function addCalendarYears(date: Date, years: number): Date {
  const targetYear = date.getFullYear() + years;
  const month = date.getMonth();
  const day = date.getDate();
  const lastDayOfMonth = new Date(targetYear, month + 1, 0).getDate();
  return new Date(targetYear, month, Math.min(day, lastDayOfMonth));
}

function dayBefore(ymd: string): string {
  const date = parseYmd(ymd.slice(0, 10));
  date.setDate(date.getDate() - 1);
  return formatYmd(date);
}

/**
 * Prior-period range for comparative P&amp;L / Balance Sheet columns.
 * Returns null when the comparative period would start before the books start
 * (P&amp;L: comparative from; Balance Sheet: as-at before books start minus one day).
 */
export function comparativeRange(
  range: DateRange,
  preset: ComparativePreset,
  fiscalYears: FinancialYear[],
  statement: ComparativeStatement = 'income',
  booksStart: string | null = null,
): DateRange | null {
  void preset;
  if (!booksStart || !range.to) return null;
  const booksStartDay = booksStart.slice(0, 10);

  if (statement === 'balanceSheet') {
    const asOf = range.to.slice(0, 10);
    const containing = fiscalYears.find(y =>
      y.startDate.slice(0, 10) <= asOf
      && y.endDate.slice(0, 10) >= asOf);
    if (!containing) return null;
    // Day before the start of the FY containing as-of — prior FY need not be stored (lazy years).
    const priorEnd = dayBefore(containing.startDate.slice(0, 10));
    // Allow as-at on books start minus one day (opening position); reject anything earlier.
    if (priorEnd < dayBefore(booksStartDay)) return null;
    return { from: '', to: priorEnd };
  }

  // Profit & Loss: same span one calendar year earlier (month-end clamped).
  if (!range.from) return null;

  const priorFrom = formatYmd(addCalendarYears(parseYmd(range.from), -1));
  const priorTo = formatYmd(addCalendarYears(parseYmd(range.to), -1));
  if (priorFrom < booksStartDay) return null;
  return { from: priorFrom, to: priorTo };
}
