import type { WizardState } from './policyDefaults';

const KEY = 'accountgo.createOrgDraft.v1';

export interface WizardDraft {
  state: WizardState;
  stepIndex: number;
}

export const loadDraft = (stepCount: number): WizardDraft | null => {
  try {
    const parsed = JSON.parse(window.localStorage.getItem(KEY) ?? 'null');
    if (!parsed || typeof parsed.state !== 'object' || !Number.isInteger(parsed.stepIndex)) return null;
    return { state: parsed.state, stepIndex: Math.min(Math.max(parsed.stepIndex, 0), stepCount - 1) };
  } catch {
    return null;
  }
};

export const saveDraft = (draft: WizardDraft): void => {
  try {
    window.localStorage.setItem(KEY, JSON.stringify(draft));
  } catch {
    // Storage may be unavailable (private mode); the draft is a convenience only.
  }
};

export const clearDraft = (): void => {
  try {
    window.localStorage.removeItem(KEY);
  } catch {
    // ignore
  }
};
