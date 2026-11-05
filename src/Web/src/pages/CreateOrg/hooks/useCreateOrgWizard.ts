import { useCallback, useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../../../auth/AuthContext';
import { setPersistedOrgSlug } from '../../../lib/storage';
import { withSuppressedEntitlementDenial } from '../../../lib/entitlementEvents';
import { freeSlotPlanError, isFreePlan, isFreeSlotUsed, type FreeSlotState } from '../../../lib/createOrgPlanOptions';
import { buildCreateReturnUrl, interpretCreateOrgResponse } from '../../../lib/createOrgSubmit';
import { usePlanCatalog } from '../../../lib/usePlanCatalog';
import type { Organization } from '../../../types';
import { clearDraft, loadDraft, saveDraft } from '../lib/draft';
import {
  buildPayload,
  defaultState,
  firstIncompleteStepIndex,
  normalizeWizardState,
  slugify,
  stepsFor,
  validateAll,
  validateStep,
  type StepErrors,
  type StepId,
  type WizardState,
} from '../lib/policyDefaults';

export type SubmitStatus = 'idle' | 'submitting' | 'success';

export const useCreateOrgWizard = (prefill: { name?: string; slug?: string; pendingSlug?: string }) => {
  const navigate = useNavigate();
  const { authFetch, refreshSession, user } = useAuth();
  const catalog = usePlanCatalog();
  // Continuation mode: a signup-created, still-unclassified workspace already exists. We initialize THAT
  // workspace (POST current/initialize) instead of creating a new one, so no duplicate is produced.
  const pendingSlug = prefill.pendingSlug?.trim() || undefined;
  const isContinuation = Boolean(pendingSlug);
  // Continuation presents About + Review; identity, plan and currency remain the stored settings.
  const steps = stepsFor(isContinuation);
  // Prefill name does not equal About consent; always resume at the first missing required choice.
  // In continuation mode the slug/name prefill is not About consent, so a saved draft (purpose/
  // collaboration, etc.) is still restored across verification/reload. For a brand-new create with a
  // name prefill, resume at the first missing choice without reusing an unrelated draft.
  const [draft] = useState(() => (prefill.name && !pendingSlug ? null : loadDraft(steps.length)));
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
    return firstIncompleteStepIndex(initial, new Date(), null, isContinuation);
  });
  const [errors, setErrors] = useState<StepErrors>({});
  const [status, setStatus] = useState<SubmitStatus>('idle');
  const [personalBooksFailed, setPersonalBooksFailed] = useState(false);
  const [businessTypeFailed, setBusinessTypeFailed] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [billingInterval, setBillingInterval] = useState<'month' | 'year'>('month');
  const [freeSlot, setFreeSlot] = useState<FreeSlotState>({ used: false, freeWorkspaceName: null, freeWorkspaceSlug: null });
  const [freeSlotMessage, setFreeSlotMessage] = useState<string | null>(null);
  const [capabilities, setCapabilities] = useState<{
    soloSetupEnabled: boolean;
    personalPurposeEnabled: boolean;
    controlledRecoveryEnabled: boolean;
    canAddPersonalBooks?: boolean;
  } | null>(null);
  // The About step waits for the server's setup capabilities so it never shows questions that the server then
  // hides (or the reverse). A failed request still unblocks the step; the server re-checks on submit.
  const [capabilitiesReady, setCapabilitiesReady] = useState(false);
  // In continuation mode, the workspace already exists: load its STORED settings so the review shows the
  // real persisted name/plan/currency/books (read-only facts), never the local draft values, which the
  // continuation does not save.
  const [existingSettings, setExistingSettings] = useState<{
    id?: string | number;
    name?: string;
    slug?: string;
    plan?: string;
    currency?: string;
    fiscalYearEnd?: string;
  } | null>(null);
  const redirectTimer = useRef<number | undefined>(undefined);

  useEffect(() => {
    if (!pendingSlug) return;
    let active = true;
    authFetch('/api/organizations/current', { headers: { 'X-AccountGo-Org': pendingSlug } })
      .then(async (res) => (res.ok ? res.json() : null))
      .then((data) => {
        if (active && data) {
          setExistingSettings({
            id: data.id,
            name: data.name,
            slug: data.slug,
            plan: data.plan,
            currency: data.currency,
            fiscalYearEnd: data.fiscalYearEnd,
          });
        }
      })
      .catch(() => {});
    return () => {
      active = false;
    };
  }, [authFetch, pendingSlug]);

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
      .catch(() => {})
      .finally(() => {
        if (active) setCapabilitiesReady(true);
      });
    return () => {
      active = false;
    };
  }, [authFetch]);

  // Free-slot usage + the owner's existing free workspace (for the plan-step message).
  useEffect(() => {
    let active = true;
    Promise.all([
      authFetch('/api/entitlements/me').then(async (res) => (res.ok ? res.json() : null)),
      authFetch('/api/organizations').then(async (res) => (res.ok ? res.json() : null)),
    ])
      .then(([me, orgs]) => {
        if (!active) return;
        const used = me?.freeWorkspace?.used as number | undefined;
        const limit = me?.freeWorkspace?.limit as number | null | undefined;
        const slotUsed = isFreeSlotUsed(used ?? 0, limit ?? null);
        let freeWorkspaceName: string | null = null;
        let freeWorkspaceSlug: string | null = null;
        if (Array.isArray(orgs) && catalog.plans) {
          const ownedFree = (orgs as Organization[]).find((org) => {
            if (org.role !== 'Owner') return false;
            if ((org.activationState || 'active') !== 'active') return false;
            const entry = catalog.plans!.find((p) => p.key === org.plan)
              || catalog.plans!.find((p) => (p.product || '').toLowerCase() === (org.plan || '').toLowerCase() && isFreePlan(p));
            // Legacy create payload still uses product aliases ("invoicing" / "accounting") for free plans.
            if (!entry) {
              return org.plan === 'accounting' || org.plan === 'invoicing';
            }
            return isFreePlan(entry);
          });
          if (ownedFree) {
            freeWorkspaceName = ownedFree.name;
            freeWorkspaceSlug = ownedFree.slug;
          }
        }
        setFreeSlot({ used: slotUsed, freeWorkspaceName, freeWorkspaceSlug });
      })
      .catch(() => {});
    return () => {
      active = false;
    };
  }, [authFetch, catalog.plans]);

  useEffect(() => () => window.clearTimeout(redirectTimer.current), []);

  useEffect(() => {
    if (status === 'idle') saveDraft({ state, stepIndex });
  }, [state, stepIndex, status]);

  const step: StepId = steps[stepIndex];

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
    if (Object.keys(found).length > 0) return;
    if (step === 'intent') {
      const planError = freeSlotPlanError(state.plan, catalog.plans, freeSlot);
      if (planError) {
        setSubmitError(planError);
        return;
      }
    }
    setStepIndex((index) => Math.min(index + 1, steps.length - 1));
  }, [step, state, capabilities, steps.length, catalog.plans, freeSlot]);

  const back = useCallback(() => {
    setErrors({});
    setSubmitError(null);
    setStepIndex((index) => Math.max(index - 1, 0));
  }, []);

  const submit = useCallback(async () => {
    const found = validateAll(state, new Date(), capabilities, isContinuation);
    if (Object.keys(found).length > 0) {
      setErrors(found);
      setStepIndex(firstIncompleteStepIndex(state, new Date(), capabilities, isContinuation));
      return;
    }
    setPersonalBooksFailed(false);
    setBusinessTypeFailed(false);
    setStatus('submitting');
    setSubmitError(null);
    try {
      const payload = buildPayload(state, capabilities);
      const shouldCreatePersonalBooks = state.addPersonalBooks === true
        && capabilities?.canAddPersonalBooks === true && capabilities.soloSetupEnabled !== false;
      const createPersonalBooks = async (companyId: unknown, currency: string) => {
        if (!shouldCreatePersonalBooks) return;
        try {
          const id = Number(companyId);
          if (!Number.isSafeInteger(id) || id <= 0) throw new Error('Workspace id unavailable');
          const personal = await authFetch('/api/organizations/personal', {
            method: 'POST', headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
              name: `${user?.firstName?.trim() || user?.email?.split('@')[0] || 'My'} — Personal`,
              currency: currency.trim().toUpperCase(), linkWorkCompanyId: id,
            }),
          });
          if (!personal.ok) throw new Error('Personal books creation failed');
        } catch {
          setPersonalBooksFailed(true);
        }
      };

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
            // Legal form uses the existing policy endpoint above; initialization keeps the stored plan.
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
            setStepIndex(steps.indexOf('review'));
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
        // Apply the chosen books type only after initialization succeeded, so an ineligible or already set-up
        // workspace is never changed. Both work kinds apply it, so a retry after switching from Company to
        // Freelance never keeps the earlier legal form. Skipped when it already matches or the ledger has postings.
        const continuationEntityType = state.workKind === 'Freelance' ? 'SoleProprietor'
          : state.workKind === 'Company' ? state.legalForm : '';
        if (continuationEntityType && capabilities?.soloSetupEnabled !== false) {
          try {
            const headers = { 'Content-Type': 'application/json', 'X-AccountGo-Org': pendingSlug };
            const current = await authFetch('/api/organizations/current/accounting-policy', { headers });
            const stored = current.ok ? await current.json().catch(() => null) : null;
            if (!stored || stored.entityType !== continuationEntityType) {
              if (stored?.isLedgerFrozen) throw new Error('Ledger has postings');
              const policy = await authFetch('/api/organizations/current/accounting-policy', {
                method: 'PUT', headers, body: JSON.stringify({ entityType: continuationEntityType }),
              });
              if (!policy.ok) throw new Error('Business type not saved');
            }
          } catch {
            setBusinessTypeFailed(true);
          }
        }
        if (shouldCreatePersonalBooks) {
          // Initialize need not return identity; resolve it under the same workspace header.
          try {
            let current = existingSettings;
            if (!current?.id) {
              const response = await authFetch('/api/organizations/current', { headers: { 'X-AccountGo-Org': pendingSlug } });
              current = response.ok ? await response.json().catch(() => null) : null;
            }
            await createPersonalBooks(data?.id ?? current?.id, current?.currency || state.currency);
          } catch {
            setPersonalBooksFailed(true);
          }
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

      const fallbackSlug = slugify(state.slug || state.name);
      const createPayload = {
        ...payload,
        returnUrl: buildCreateReturnUrl(window.location.origin, fallbackSlug, billingInterval),
      };
      const response = await withSuppressedEntitlementDenial(() =>
        authFetch('/api/organizations', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(createPayload),
        }),
      );
      const data = await response.json().catch(() => null);
      const failureReason = data?.reasonCode ?? data?.extensions?.reasonCode;
      if (response.status === 409) {
        setErrors({ slug: 'That URL is already taken. Choose another.' });
        setStepIndex(steps.indexOf('identity'));
        setStatus('idle');
        return;
      }
      if (response.status === 422 && failureReason === 'solo_setup_disabled') {
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
        setStepIndex(steps.indexOf('review'));
        setStatus('idle');
        return;
      }

      const outcome = interpretCreateOrgResponse(response.status, data, fallbackSlug);
      if (outcome.kind === 'quota-free-slot') {
        setFreeSlotMessage(outcome.detail);
        setFreeSlot((prev) => ({ ...prev, used: true }));
        setStepIndex(steps.indexOf('intent'));
        setStatus('idle');
        return;
      }
      if (outcome.kind === 'error') {
        setSubmitError(outcome.detail);
        setStatus('idle');
        return;
      }
      await createPersonalBooks(data?.id, state.currency);
      if (outcome.kind === 'redirect-checkout') {
        clearDraft();
        window.location.assign(outcome.url);
        return;
      }
      if (outcome.kind === 'pending') {
        setPersistedOrgSlug(outcome.slug);
        clearDraft();
        const q = outcome.checkoutError
          ? `?checkoutError=${encodeURIComponent(outcome.checkoutError)}`
          : '';
        navigate(`/org/${encodeURIComponent(outcome.slug)}${q}`, { replace: true });
        return;
      }
      setPersistedOrgSlug(outcome.slug);
      clearDraft();
      setStatus('success');
      redirectTimer.current = window.setTimeout(() => navigate(`/org/${encodeURIComponent(outcome.slug)}/dashboard`, { replace: true }), 900);
    } catch {
      setSubmitError('We could not reach the server. Check your connection and try again.');
      setStatus('idle');
    }
  }, [state, capabilities, authFetch, navigate, pendingSlug, refreshSession, isContinuation, steps, billingInterval, user, existingSettings]);

  return {
    state, update, step, stepIndex, stepCount: steps.length, errors, status, submitError, personalBooksFailed, businessTypeFailed, next, back, submit,
    capabilities, capabilitiesReady, isContinuation, existingSettings, freeSlot, freeSlotMessage, billingInterval, setBillingInterval,
    catalog,
  };
};
