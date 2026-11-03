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

export const useCreateOrgWizard = (prefill: { name?: string; slug?: string; pendingSlug?: string }) => {
  const navigate = useNavigate();
  const { authFetch, refreshSession } = useAuth();
  // Continuation mode: a signup-created, still-unclassified workspace already exists. We initialize THAT
  // workspace (POST current/initialize) instead of creating a new one, so no duplicate is produced.
  const pendingSlug = prefill.pendingSlug?.trim() || undefined;
  // Prefill name does not equal About consent; always resume at the first missing required choice.
  // In continuation mode the slug/name prefill is not About consent, so a saved draft (purpose/
  // collaboration, etc.) is still restored across verification/reload. For a brand-new create with a
  // name prefill, resume at the first missing choice without reusing an unrelated draft.
  const [draft] = useState(() => (prefill.name && !pendingSlug ? null : loadDraft(STEPS.length)));
  const [state, setState] = useState<WizardState>(() => {
    const base = defaultState(new Date(), prefill);
    if (draft?.state) {
      // Keep the draft's answers but let the prefill (the existing workspace's identity) win for name/slug.
      const restored = normalizeWizardState(draft.state);
      return pendingSlug ? { ...restored, name: base.name, slug: base.slug, slugTouched: base.slugTouched } : restored;
    }
    return base;
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

      // Continuation: initialize the existing signup-created workspace in place (no duplicate).
      if (pendingSlug) {
        const response = await authFetch('/api/organizations/current/initialize', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', 'X-AccountGo-Org': pendingSlug },
          body: JSON.stringify({
            workspacePurpose: payload.workspacePurpose,
            collaborationIntent: payload.collaborationIntent,
            ownerAccessPreset: payload.ownerAccessPreset,
            initialJournalWorkflow: payload.initialJournalWorkflow,
          }),
        });
        const data = await response.json().catch(() => null);
        if (!response.ok) {
          const failureReason = data?.reasonCode ?? data?.extensions?.reasonCode;
          if (response.status === 422 && failureReason === 'solo_setup_disabled') {
            setCapabilities((prev) => ({
              soloSetupEnabled: false,
              personalPurposeEnabled: prev?.personalPurposeEnabled ?? false,
              controlledRecoveryEnabled: prev?.controlledRecoveryEnabled ?? false,
            }));
            setSubmitError('Setup is not enabled for this workspace yet. Please try again later.');
            setStepIndex(STEPS.indexOf('review'));
          } else if (response.status === 422 && failureReason === 'not_pending_signup_workspace') {
            // This workspace cannot use the signup continuation; send the owner to a fresh create instead.
            setSubmitError('This workspace is already set up or is not eligible for guided setup. Create a new workspace to continue.');
          } else if (response.status === 403) {
            setSubmitError('Only the workspace owner can finish setup. Sign in as the owner and try again.');
          } else {
            setSubmitError(data?.detail || data?.title || data?.message || 'We could not finish setting up your workspace. Try again.');
          }
          setStatus('idle');
          return;
        }
        // Refresh the session so the just-granted workspace access is reflected before landing on the
        // dashboard (the new Full-access grant and single-actor rule take effect on the next token).
        await Promise.resolve(refreshSession()).catch(() => {});
        setPersistedOrgSlug(pendingSlug);
        clearDraft();
        setStatus('success');
        redirectTimer.current = window.setTimeout(() => navigate(`/org/${encodeURIComponent(pendingSlug)}/dashboard`, { replace: true }), 900);
        return;
      }

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
  }, [state, capabilities, authFetch, navigate, pendingSlug, refreshSession]);

  return { state, update, step, stepIndex, stepCount: STEPS.length, errors, status, submitError, next, back, submit, capabilities };
};
