export type FiscalYearPattern = 'Calendar' | 'AprMar' | 'JulJun' | 'Custom';

export interface FyPreview {
  code: string;
  start: string;
  end: string;
  label: string;
}

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

export const parseIsoDate = (value: string): { year: number; month: number; day: number } | null => {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (!match) return null;
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  const probe = new Date(Date.UTC(year, month - 1, day));
  if (probe.getUTCFullYear() !== year || probe.getUTCMonth() !== month - 1 || probe.getUTCDate() !== day) return null;
  return { year, month, day };
};

export const fiscalYearStartMonth = (pattern: FiscalYearPattern, customMonth: number | null): number | null => {
  if (pattern === 'Calendar') return 1;
  if (pattern === 'AprMar') return 4;
  if (pattern === 'JulJun') return 7;
  return customMonth !== null && Number.isInteger(customMonth) && customMonth >= 1 && customMonth <= 12 ? customMonth : null;
};

const pad = (value: number) => String(value).padStart(2, '0');
const formatDay = (year: number, month: number, day: number) => `${day} ${MONTHS[month - 1]} ${year}`;

// Mirrors Core.Domain.FiscalYearCalculator so the preview matches what the API provisions.
export const fiscalYearPreview = (
  booksStart: string,
  pattern: FiscalYearPattern,
  customMonth: number | null,
): FyPreview | null => {
  const date = parseIsoDate(booksStart);
  const month = fiscalYearStartMonth(pattern, customMonth);
  if (!date || month === null) return null;

  const startYear = date.month >= month ? date.year : date.year - 1;
  const endYear = month === 1 ? startYear : startYear + 1;
  const endMonth = month === 1 ? 12 : month - 1;
  const lastDay = new Date(Date.UTC(endYear, endMonth, 0)).getUTCDate();
  const code = month === 1 ? `FY${startYear}` : `FY${startYear}-${pad((startYear + 1) % 100)}`;

  return {
    code,
    start: `${startYear}-${pad(month)}-01`,
    end: `${endYear}-${pad(endMonth)}-${pad(lastDay)}`,
    label: `${code} · ${formatDay(startYear, month, 1)} – ${formatDay(endYear, endMonth, lastDay)} · Open`,
  };
};
