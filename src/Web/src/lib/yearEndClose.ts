/** Fiscal year close status from api/Financials/YearEnd (FiscalYearCloseStatus). */
export type FiscalYearCloseStatusValue = 1 | 2 | 3 | 'Open' | 'Ended' | 'Closed';

const STATUS_LABELS: Record<number, string> = {
  1: 'Open',
  2: 'Ended — ready to close',
  3: 'Closed',
};

export function fiscalYearStatusLabel(status: FiscalYearCloseStatusValue): string {
  if (typeof status === 'string') {
    if (status === 'Open') return STATUS_LABELS[1];
    if (status === 'Ended') return STATUS_LABELS[2];
    if (status === 'Closed') return STATUS_LABELS[3];
    return status;
  }
  return STATUS_LABELS[status] ?? 'Unknown';
}

/** Settings fiscal-year badge. Never treats a missing status or the active flag as Closed. */
export function fiscalYearReadStatusLabel(status: string | null | undefined): 'Open' | 'Ended' | 'Closed' {
  if (status === 'Open' || status === 'Ended' || status === 'Closed') return status;
  return 'Open';
}

export function normalizeFiscalYearCloseStatus(status: FiscalYearCloseStatusValue): number {
  if (typeof status === 'number') return status;
  switch (status) {
    case 'Open':
      return 1;
    case 'Ended':
      return 2;
    case 'Closed':
      return 3;
    default:
      return 0;
  }
}

export const reviewAndCloseLabel = 'Review & close';

/** Org-relative close links stay inside the current workspace. */
export function closeResolutionUrl(orgSlug: string, path: string): string {
  if (!orgSlug || path.charAt(0) !== '/' || path.startsWith('//') || path.includes('://')) {
    throw new Error('Close links stay inside this workspace.');
  }
  return `/org/${encodeURIComponent(orgSlug)}${path}`;
}

export function warningCloseReady(warningCount: number, acknowledged: boolean, reason: string): boolean {
  if (warningCount === 0) return true;
  return acknowledged && reason.trim().length > 0 && reason.trim().length <= 2000;
}

export function canEnableCloseButton(options: {
  status: FiscalYearCloseStatusValue;
  blockingCount: number;
  canClosePermission: boolean;
}): boolean {
  if (!options.canClosePermission) return false;
  const normalized = normalizeFiscalYearCloseStatus(options.status);
  if (normalized === 3) return false;
  return options.blockingCount === 0;
}
