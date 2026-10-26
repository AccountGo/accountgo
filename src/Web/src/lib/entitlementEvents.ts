/** Problem codes the server returns when the plan, not the user's role, blocks an action. */
export const ENTITLEMENT_CODES = ['QUOTA_EXCEEDED', 'MODULE_NOT_SUBSCRIBED', 'PLAN_NOT_AVAILABLE'] as const;
export type EntitlementCode = (typeof ENTITLEMENT_CODES)[number];

export interface EntitlementProblem {
  code: EntitlementCode;
  title?: string;
  detail?: string;
  resource?: string;
  limit?: number;
  used?: number;
  period?: string | null;
  contact?: string | null;
}

export const ENTITLEMENT_DENIED_EVENT = 'accountgo:entitlement-denied';

export function isEntitlementProblem(body: unknown): body is EntitlementProblem {
  const code = (body as { code?: unknown } | null)?.code;
  return typeof code === 'string' && (ENTITLEMENT_CODES as readonly string[]).includes(code);
}

/**
 * Inspects a response without consuming it and announces plan denials, so one dialog can explain them wherever
 * the request came from. Pages still show their own inline errors.
 */
export async function announceEntitlementDenial(response: Response): Promise<void> {
  if (response.status !== 403 || typeof window === 'undefined') return;
  const type = response.headers.get('content-type') || '';
  if (!type.includes('json')) return;
  try {
    const body = await response.clone().json();
    if (isEntitlementProblem(body)) {
      window.dispatchEvent(new CustomEvent<EntitlementProblem>(ENTITLEMENT_DENIED_EVENT, { detail: body }));
    }
  } catch {
    /* not a problem document */
  }
}
