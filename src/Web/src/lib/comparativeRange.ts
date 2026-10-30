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

function addCalendarYears(date: Date, years: number): Date {
  return new Date(date.getFullYear() + years, date.getMonth(), date.getDate());
}

function booksStartIso(fiscalYears: FinancialYear[]): string | null {
  if (!fiscalYears.length) return null;
  return fiscalYears
    .map(y => y.startDate.slice(0, 10))
    .sort()[0];
}

/**
 * Prior-period range for comparative P&amp;L / Balance Sheet columns.
 * Returns null when the comparative period would start before the books start.
 */
export function comparativeRange(
  range: DateRange,
  preset: ComparativePreset,
  fiscalYears: FinancialYear[],
  statement: ComparativeStatement = 'income',
): DateRange | null {
  const booksStart = booksStartIso(fiscalYears);
  if (!booksStart || !range.to) return null;

  if (statement === 'balanceSheet') {
    const asOf = parseYmd(range.to);
    const containing = fiscalYears.find(y =>
      y.startDate.slice(0, 10) <= range.to
      && y.endDate.slice(0, 10) >= range.to);
    if (!containing) return null;
    const prior = fiscalYears
      .filter(y => y.endDate.slice(0, 10) < containing.startDate.slice(0, 10))
      .sort((a, b) => b.endDate.localeCompare(a.endDate))[0];
    if (!prior) return null;
    const priorEnd = prior.endDate.slice(0, 10);
    if (priorEnd < booksStart) return null;
    // Balance sheet comparative is a point-in-time: as at end of previous fiscal year.
    void asOf;
    void preset;
    return { from: '', to: priorEnd };
  }

  // Profit & Loss: same span one fiscal/calendar year earlier.
  if (!range.from) return null;

  const from = parseYmd(range.from);
  const to = parseYmd(range.to);
  const priorFrom = formatYmd(addCalendarYears(from, -1));
  const priorTo = formatYmd(addCalendarYears(to, -1));
  if (priorFrom < booksStart) return null;
  return { from: priorFrom, to: priorTo };
}
