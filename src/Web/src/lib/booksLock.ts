/** True when the lock date is moved earlier or cleared (reopen) — a reason is required. */
export function isBooksLockReopen(
  previousLockedThrough: string | null | undefined,
  nextLockedThrough: string | null | undefined,
): boolean {
  const previous = previousLockedThrough?.trim() || null;
  const next = nextLockedThrough?.trim() || null;
  if (!next) return previous != null;
  if (!previous) return false;
  return next < previous;
}

export function formatBooksLockDate(value: string | null | undefined): string {
  if (!value) return 'unlocked';
  return value;
}
