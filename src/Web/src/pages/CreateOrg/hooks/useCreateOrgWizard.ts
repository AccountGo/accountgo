import { useCallback, useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../../../auth/AuthContext';
import { setPersistedOrgSlug } from '../../../lib/storage';
import { clearDraft, loadDraft, saveDraft } from '../lib/draft';
import {
  buildPayload,
  defaultState,
  slugify,
  STEPS,
  validateAll,
  validateStep,
  type StepErrors,
  type StepId,
  type WizardState,
} from '../lib/policyDefaults';

export type SubmitStatus = 'idle' | 'submitting' | 'success';

export const useCreateOrgWizard = (prefill: { name?: string; slug?: string }) => {
  const navigate = useNavigate();
  const { authFetch } = useAuth();
  // A name from signup wins over any saved draft; otherwise resume where the user left off.
  const [draft] = useState(() => (prefill.name ? null : loadDraft(STEPS.length)));
  const [state, setState] = useState<WizardState>(() => draft?.state ?? defaultState(new Date(), prefill));
  const [stepIndex, setStepIndex] = useState(() => draft?.stepIndex ?? (prefill.name ? 1 : 0));
  const [errors, setErrors] = useState<StepErrors>({});
  const [status, setStatus] = useState<SubmitStatus>('idle');
  const [submitError, setSubmitError] = useState<string | null>(null);
  const redirectTimer = useRef<number | undefined>(undefined);

  useEffect(() => () => window.clearTimeout(redirectTimer.current), []);

  useEffect(() => {
    if (status === 'idle') saveDraft({ state, stepIndex });
  }, [state, stepIndex, status]);

  const step: StepId = STEPS[stepIndex];

  const update = useCallback((patch: Partial<WizardState>) => {
    setState((previous) => {
      const next = { ...previous, ...patch };
      // The slug follows the name until the user edits it directly.
      if (patch.name !== undefined && !previous.slugTouched) next.slug = slugify(patch.name);
      if (patch.slug !== undefined) {
        next.slug = slugify(patch.slug);
        next.slugTouched = true;
      }
      return next;
    });
    setErrors({});
    setSubmitError(null);
  }, []);

  const next = useCallback(() => {
    const found = validateStep(step, state);
    setErrors(found);
    if (Object.keys(found).length === 0) setStepIndex((index) => Math.min(index + 1, STEPS.length - 1));
  }, [step, state]);

  const back = useCallback(() => {
    setErrors({});
    setSubmitError(null);
    setStepIndex((index) => Math.max(index - 1, 0));
  }, []);

  const submit = useCallback(async () => {
    const found = validateAll(state);
    if (Object.keys(found).length > 0) {
      setErrors(found);
      setStepIndex(STEPS.indexOf(found.name || found.slug ? 'identity' : 'moneyTime'));
      return;
    }
    setStatus('submitting');
    setSubmitError(null);
    try {
      const response = await authFetch('/api/organizations', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(buildPayload(state)),
      });
      const data = await response.json().catch(() => null);
      if (!response.ok) {
        if (response.status === 409) {
          setErrors({ slug: 'That URL is already taken. Choose another.' });
          setStepIndex(STEPS.indexOf('identity'));
        } else {
          setSubmitError(data?.detail || data?.title || data?.message || 'We could not create the workspace. Try again.');
        }
        setStatus('idle');
        return;
      }
      const slug: string = data?.slug || slugify(state.slug || state.name);
      setPersistedOrgSlug(slug);
      clearDraft();
      setStatus('success');
      redirectTimer.current = window.setTimeout(() => navigate(`/org/${encodeURIComponent(slug)}/dashboard`, { replace: true }), 900);
    } catch {
      setSubmitError('We could not reach the server. Check your connection and try again.');
      setStatus('idle');
    }
  }, [state, authFetch, navigate]);

  return { state, update, step, stepIndex, stepCount: STEPS.length, errors, status, submitError, next, back, submit };
};
