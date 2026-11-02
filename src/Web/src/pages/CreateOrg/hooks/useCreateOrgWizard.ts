import { useCallback, useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../../../auth/AuthContext';
import { setPersistedOrgSlug } from '../../../lib/storage';
import { clearDraft, loadDraft, saveDraft } from '../lib/draft';
import {
  buildPayload,
  defaultState,
  firstIncompleteStepIndex,
  normalizeWizardState,
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
  // Prefill name does not equal About consent; always resume at the first missing required choice.
  const [draft] = useState(() => (prefill.name ? null : loadDraft(STEPS.length)));
  const [state, setState] = useState<WizardState>(() => {
    if (draft?.state) return normalizeWizardState(draft.state);
    return defaultState(new Date(), prefill);
  });
  const [stepIndex, setStepIndex] = useState(() => {
    const initial = draft ? normalizeWizardState(draft.state) : defaultState(new Date(), prefill);
    return firstIncompleteStepIndex(initial);
  });
  const [errors, setErrors] = useState<StepErrors>({});
  const [status, setStatus] = useState<SubmitStatus>('idle');
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [capabilities, setCapabilities] = useState<{
    soloSetupEnabled: boolean;
    personalPurposeEnabled: boolean;
    controlledRecoveryEnabled: boolean;
  } | null>(null);
  const redirectTimer = useRef<number | undefined>(undefined);

  useEffect(() => {
    let active = true;
    authFetch('/api/organizations/setup-capabilities')
      .then(async (res) => (res.ok ? res.json() : null))
      .then((data) => {
        if (active && data) {
          setCapabilities(data);
          if (data.personalPurposeEnabled === false) {
            setState((prev) => (prev.workspacePurpose === 'Personal' ? { ...prev, workspacePurpose: '' } : prev));
          }
          if (data.soloSetupEnabled === false) {
            setState((prev) => ({ ...prev, workspacePurpose: '', collaborationIntent: '' }));
          }
        }
      })
      .catch(() => {});
    return () => {
      active = false;
    };
  }, [authFetch]);

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
    const found = validateStep(step, state, new Date(), capabilities);
    setErrors(found);
    if (Object.keys(found).length === 0) setStepIndex((index) => Math.min(index + 1, STEPS.length - 1));
  }, [step, state, capabilities]);

  const back = useCallback(() => {
    setErrors({});
    setSubmitError(null);
    setStepIndex((index) => Math.max(index - 1, 0));
  }, []);

  const submit = useCallback(async () => {
    const found = validateAll(state, new Date(), capabilities);
    if (Object.keys(found).length > 0) {
      setErrors(found);
      setStepIndex(firstIncompleteStepIndex(state, new Date(), capabilities));
      return;
    }
    setStatus('submitting');
    setSubmitError(null);
    try {
      const payload = buildPayload(state, capabilities);
      const response = await authFetch('/api/organizations', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      });
      const data = await response.json().catch(() => null);
      if (!response.ok) {
        const failureReason = data?.reasonCode ?? data?.extensions?.reasonCode;
        if (response.status === 409) {
          setErrors({ slug: 'That URL is already taken. Choose another.' });
          setStepIndex(STEPS.indexOf('identity'));
        } else if (response.status === 422 && failureReason === 'solo_setup_disabled') {
          setCapabilities((prev) => ({
            soloSetupEnabled: false,
            personalPurposeEnabled: prev?.personalPurposeEnabled ?? false,
            controlledRecoveryEnabled: prev?.controlledRecoveryEnabled ?? false,
          }));
          setState((prev) => ({ ...prev, workspacePurpose: '', collaborationIntent: '' }));
          void authFetch('/api/organizations/setup-capabilities')
            .then(async (res) => (res.ok ? res.json() : null))
            .then((fresh) => {
              if (fresh) setCapabilities(fresh);
            })
            .catch(() => {});
          setSubmitError(
            'Solo setup is not enabled. Review and confirm the updated access and accounting policy before creating your workspace.',
          );
          setStepIndex(STEPS.indexOf('review'));
        } else if (response.status === 422 && failureReason === 'personal_purpose_disabled') {
          setSubmitError(data?.detail || 'Personal finances workspaces are not available yet. Choose business or freelance work, or try again later.');
          setStepIndex(STEPS.indexOf('about'));
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
  }, [state, capabilities, authFetch, navigate]);

  return { state, update, step, stepIndex, stepCount: STEPS.length, errors, status, submitError, next, back, submit, capabilities };
};
