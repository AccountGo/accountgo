/**
 * Shared lifecycle for MFA sign-in submissions (passkey, authenticator code, recovery code).
 *
 * Stages:
 * - `prompting`  – waiting for the browser / authenticator (passkey ceremony only)
 * - `verifying`  – the server is checking the proof
 * - `signing-in` – the server accepted the proof; the caller is establishing the session and routing
 *
 * The submission is only released back to `idle` when something fails. A successful verification stays
 * locked in `signing-in` until the caller navigates away, so a slow post-auth hand-off (workspace
 * resolution, invitation acceptance) can never re-enable the button and invite a second ceremony.
 */
export type MfaSubmitStage = 'idle' | 'prompting' | 'verifying' | 'signing-in';

export interface RunMfaSignInOptions<T> {
  /** Performs the verification. Call `setStage` to report progress (e.g. `prompting` → `verifying`). */
  verify: (setStage: (stage: MfaSubmitStage) => void) => Promise<T>;
  /** Completes the session. Awaited: the submission stays locked until it settles. */
  onSuccess: (result: T) => void | Promise<void>;
  setStage: (stage: MfaSubmitStage) => void;
  onError: (message: string) => void;
  fallbackError: string;
}

/** Returns `true` when verification and the success hand-off both completed. */
export async function runMfaSignIn<T>({
  verify,
  onSuccess,
  setStage,
  onError,
  fallbackError,
}: RunMfaSignInOptions<T>): Promise<boolean> {
  try {
    const result = await verify(setStage);
    setStage('signing-in');
    await onSuccess(result);
    return true;
  } catch (err: unknown) {
    const message = err instanceof Error && err.message ? err.message : fallbackError;
    onError(message);
    setStage('idle');
    return false;
  }
}
