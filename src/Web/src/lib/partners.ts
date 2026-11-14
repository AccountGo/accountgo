export type PartnerSetupRow = { name: string; percent: number; capital: number; drawings: number };

export function localDateInputValue(date: Date): string {
  const pad = (value: number) => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

export function percentageTotal(percentages: number[]): number {
  return Math.round(percentages.reduce((sum, value) => sum + Math.round(value * 10_000), 0)) / 10_000;
}

export function splitAmount(amount: number, shares: Array<{ id: number; percent: number }>): number[] {
  const sign = amount < 0 ? -1 : 1;
  const cents = Math.round(Math.abs(amount) * 100);
  const allocations = shares.map((share, index) => {
    const exact = cents * share.percent / 100;
    const floor = Math.floor(exact);
    return { index, id: share.id, cents: floor, remainder: exact - floor };
  });
  let left = cents - allocations.reduce((sum, item) => sum + item.cents, 0);
  for (const item of [...allocations].sort((a, b) => b.remainder - a.remainder || a.id - b.id).slice(0, left)) item.cents++;
  return allocations.map(item => sign * item.cents / 100);
}

export function splitPrefill(total: number, count: number): number[] {
  if (count <= 0) return [];
  const per = Math.trunc(Math.round(total * 100) / count);
  const result = Array.from({ length: count }, () => per / 100);
  result[count - 1] = Math.round((Math.round(total * 100) - per * (count - 1)) / 100 * 100) / 100;
  return result;
}

export function validatePartnerSetup(rows: PartnerSetupRow[], capitalBalance: number, drawingsBalance: number): string | null {
  if (rows.length < 2) return 'At least two partners are required.';
  if (rows.some(row => !row.name.trim() || row.percent <= 0)) return 'Each partner needs a name and a positive share.';
  if (percentageTotal(rows.map(row => row.percent)) !== 100) return 'Profit-sharing ratios must total 100%.';
  if (Math.round(rows.reduce((sum, row) => sum + row.capital, 0) * 100) !== Math.round(capitalBalance * 100)
      || Math.round(rows.reduce((sum, row) => sum + row.drawings, 0) * 100) !== Math.round(drawingsBalance * 100)) {
    return `Partner capital must add up to ${capitalBalance.toFixed(2)} and drawings to ${drawingsBalance.toFixed(2)}.`;
  }
  return null;
}
