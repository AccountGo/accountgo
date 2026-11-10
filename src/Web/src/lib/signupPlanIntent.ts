// SPDX-License-Identifier: AGPL-3.0-only
//
// A plan chosen before signing up (e.g. /signup?plan=<key> from a pricing page) is remembered for a day
// and offered once workspace setup finishes. It only steers navigation: the server still decides which
// plans are available and validates every change.

const STORAGE_KEY = 'accountgo:signup-plan';
const MAX_AGE_MS = 24 * 60 * 60 * 1000;
const PLAN_KEY = /^[a-z][a-z0-9_]{1,40}$/;

type Stored = { plan: string; at: number };

function storage(): Storage | null {
  try {
    return typeof window === 'undefined' ? null : window.localStorage;
  } catch {
    return null;
  }
}

/** Remember ?plan= from the signup URL. Ignores anything that is not a plain plan key. */
export function rememberSignupPlan(search: string, now = Date.now()): string | null {
  const plan = new URLSearchParams(search).get('plan')?.trim().toLowerCase() ?? '';
  if (!PLAN_KEY.test(plan)) return null;
  try {
    storage()?.setItem(STORAGE_KEY, JSON.stringify({ plan, at: now } satisfies Stored));
  } catch {
    /* storage unavailable: the plan is simply not offered later */
  }
  return plan;
}

/** Read and clear the remembered plan. Expired or malformed values are dropped. */
export function takeSignupPlan(now = Date.now()): string | null {
  const store = storage();
  if (!store) return null;
  try {
    const raw = store.getItem(STORAGE_KEY);
    store.removeItem(STORAGE_KEY);
    if (!raw) return null;
    const value = JSON.parse(raw) as Partial<Stored>;
    if (typeof value.plan !== 'string' || !PLAN_KEY.test(value.plan)) return null;
    if (typeof value.at !== 'number' || now - value.at > MAX_AGE_MS || value.at > now) return null;
    return value.plan;
  } catch {
    return null;
  }
}

/** Where to land after setup: the subscription settings with the chosen plan highlighted, or the dashboard. */
export function postSetupPath(slug: string, plan: string | null): string {
  const base = `/org/${encodeURIComponent(slug)}`;
  return plan ? `${base}/settings?tab=subscription&upgrade=${encodeURIComponent(plan)}` : `${base}/dashboard`;
}
