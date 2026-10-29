// Payment terms count business days: Monday to Friday, with no holiday calendar. Must match Core.Domain.BusinessDays
// on the API, which sets the due date when an invoice is saved without one. Dates are 'YYYY-MM-DD' strings and are
// handled in UTC so the local time zone never shifts a day.

const parse = (date: string) => {
  const [y, m, d] = date.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d));
};
const format = (date: Date) => date.toISOString().slice(0, 10);
const isWeekend = (date: Date) => date.getUTCDay() === 0 || date.getUTCDay() === 6;

/** The date `days` business days after `start`; 0 is `start` itself. */
export function addBusinessDays(start: string, days: number): string {
  const date = parse(start);
  let remaining = Math.max(0, days);
  while (remaining > 0) {
    date.setUTCDate(date.getUTCDate() + 1);
    if (!isWeekend(date)) remaining--;
  }
  return format(date);
}

/** Business days from `start` to `end` (the start day not counted); null when `end` is before `start`. */
export function businessDaysBetween(start: string, end: string): number | null {
  const from = parse(start);
  const to = parse(end);
  if (to < from) return null;
  let count = 0;
  while (from < to) {
    from.setUTCDate(from.getUTCDate() + 1);
    if (!isWeekend(from)) count++;
  }
  return count;
}

export const isValidDate = (value: string) => /^\d{4}-\d{2}-\d{2}$/.test(value) && !isNaN(parse(value).getTime());

/**
 * Today's date where the user is, as 'YYYY-MM-DD': the default for a document or payment date. Not toISOString(),
 * which is the UTC date and is still yesterday (or already tomorrow) for part of the day in most time zones.
 */
export function localToday(): string {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
}
