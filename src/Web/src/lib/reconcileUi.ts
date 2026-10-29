/** Pure helpers for the reconciliation workspace (keyboard focus, search, tick batches). */

export interface ReconcileLineRef {
  lineId: number;
  in: number;
  out: number;
  ticked: boolean;
  type: string;
  reference?: string | null;
  payee?: string | null;
}

/** Move focus within a flattened list; wraps at the ends. */
export function nextFocusIndex(length: number, current: number, delta: number): number {
  if (length <= 0) return 0;
  const start = Number.isFinite(current) ? current : 0;
  return (start + delta + length * 1000) % length;
}

/** Case-insensitive match on amount (exact to 2dp), type, reference, or payee. */
export function lineMatchesSearch(line: ReconcileLineRef, rawQuery: string): boolean {
  const query = rawQuery.trim().toLowerCase();
  if (!query) return true;

  const amount = line.in || line.out;
  const amountText = amount.toFixed(2);
  if (amountText.includes(query) || String(amount).includes(query)) return true;

  const haystack = [line.type, line.reference ?? '', line.payee ?? ''].join(' ').toLowerCase();
  return haystack.includes(query);
}

export function filterVisibleLines(
  lines: ReconcileLineRef[],
  search: string,
  hideTicked: boolean,
): ReconcileLineRef[] {
  return lines.filter((line) => {
    if (hideTicked && line.ticked) return false;
    return lineMatchesSearch(line, search);
  });
}

/** Build add/remove arrays for PUT Items from the previous ticked set and a toggled line. */
export function tickTogglePayload(
  previouslyTicked: ReadonlySet<number>,
  lineId: number,
): { add: number[]; remove: number[] } {
  if (previouslyTicked.has(lineId)) {
    return { add: [], remove: [lineId] };
  }
  return { add: [lineId], remove: [] };
}

/** Server difference must be exactly 0.00 before Finish is enabled. */
export function canFinishReconciliation(difference: number): boolean {
  return Math.round((difference + Number.EPSILON) * 100) === 0;
}

export const RECONCILE_TICK_DEBOUNCE_MS = 400;
