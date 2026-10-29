import { useCallback, useState } from 'react';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { parseReconciledPeriodConflict } from './reconciledPeriod';

interface PendingConfirmation {
  message: string;
  retry: () => void;
}

/**
 * Call handleConflict(response, retry) after a write. When the API answers 409 RECONCILED_PERIOD_CONFIRM_REQUIRED
 * it opens the dialog and returns true (the caller stops); confirming runs retry, which resends with the flag.
 */
export function useReconciledPeriodConfirm() {
  const [pending, setPending] = useState<PendingConfirmation | null>(null);

  const handleConflict = useCallback(async (response: Response, retry: () => void): Promise<boolean> => {
    if (response.status !== 409) return false;
    const body = await response.clone().json().catch(() => null);
    const prompt = parseReconciledPeriodConflict(response.status, body);
    if (!prompt) return false;
    setPending({ message: prompt.message, retry });
    return true;
  }, []);

  const dialog = pending ? (
    <ConfirmDialog
      title="Post into a reconciled period?"
      message={pending.message}
      confirmLabel="Post anyway"
      onConfirm={() => {
        const { retry } = pending;
        setPending(null);
        retry();
      }}
      onCancel={() => setPending(null)}
    />
  ) : null;

  return { handleConflict, dialog };
}
