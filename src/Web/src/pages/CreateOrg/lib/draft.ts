import { normalizeWizardState, type WizardState } from './policyDefaults';

/** Bumped when About answers became required; v1 drafts resume without inventing those answers. */
const KEY = 'accountgo.createOrgDraft.v2';
const LEGACY_KEY = 'accountgo.createOrgDraft.v1';

export interface WizardDraft {
  state: WizardState;
  stepIndex: number;
}

export const loadDraft = (stepCount: number): WizardDraft | null => {
  try {
    const raw = window.localStorage.getItem(KEY) ?? window.localStorage.getItem(LEGACY_KEY);
    const parsed = JSON.parse(raw ?? 'null');
    if (!parsed || typeof parsed.state !== 'object' || !Number.isInteger(parsed.stepIndex)) return null;
    const state = normalizeWizardState(parsed.state);
    return { state, stepIndex: Math.min(Math.max(parsed.stepIndex, 0), stepCount - 1) };
  } catch {
    return null;
  }
};

export const saveDraft = (draft: WizardDraft): void => {
  try {
    window.localStorage.setItem(KEY, JSON.stringify(draft));
    window.localStorage.removeItem(LEGACY_KEY);
  } catch {
    // Storage may be unavailable (private mode); the draft is a convenience only.
  }
};

export const clearDraft = (): void => {
  try {
    window.localStorage.removeItem(KEY);
    window.localStorage.removeItem(LEGACY_KEY);
  } catch {
    // ignore
  }
};
