export const RECONCILED_PERIOD_CONFIRM_REQUIRED = 'RECONCILED_PERIOD_CONFIRM_REQUIRED';
export const CONFIRM_RECONCILED_PERIOD_PARAM = 'confirmReconciledPeriod';

export interface ReconciledPeriodPrompt {
  message: string;
  bankName?: string;
  statementEndDate?: string;
  postingDate?: string;
}

/** Appends confirmReconciledPeriod=true when the user has accepted the warning. */
export function withConfirmReconciledPeriod(url: string, confirmed: boolean): string {
  if (!confirmed) return url;
  const separator = url.includes('?') ? '&' : '?';
  return `${url}${separator}${CONFIRM_RECONCILED_PERIOD_PARAM}=true`;
}

/** Reads the 409 the API returns when a posting lands in a reconciled period; null for any other response. */
export function parseReconciledPeriodConflict(status: number, body: unknown): ReconciledPeriodPrompt | null {
  if (status !== 409 || !body || typeof body !== 'object') return null;
  const record = body as Record<string, unknown>;
  if (record.code !== RECONCILED_PERIOD_CONFIRM_REQUIRED) return null;
  const text = (value: unknown) => (typeof value === 'string' && value ? value : undefined);
  return {
    message:
      text(record.message) ??
      text(record.detail) ??
      'This posts into a period that is already reconciled. Continue anyway?',
    bankName: text(record.bankName),
    statementEndDate: text(record.statementEndDate),
    postingDate: text(record.postingDate),
  };
}
