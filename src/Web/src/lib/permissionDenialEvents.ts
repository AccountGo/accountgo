import { classifyResponse, type ClassifiedProblem } from './apiProblems';
import { isDenialAnnouncementSuppressed } from './entitlementEvents';

/**
 * A server answer that says the member lacks a permission, announced from the one place every call goes through (authFetch) so
 * the workspace shell can refresh the member's permissions and, for an action, explain what is needed in a non-blocking notice.
 * It carries only the classified problem (permission key, code, request id): no document text, no field values.
 */
export const PERMISSION_DENIED_EVENT = 'accountgo:permission-denied';

export interface PermissionDeniedDetail {
  problem: ClassifiedProblem;
  /** True for a write the member just tried (save, pay, void...). A read that was denied is already explained by the page. */
  action: boolean;
}

/** Inspects a response without consuming it and announces a permission denial. Never throws. */
export async function announcePermissionDenial(response: Response, options: { action: boolean }): Promise<void> {
  if (response.status !== 403 || typeof window === 'undefined' || isDenialAnnouncementSuppressed()) return;
  try {
    const problem = await classifyResponse(response);
    if (problem?.category !== 'permission-denied') return;
    window.dispatchEvent(new CustomEvent<PermissionDeniedDetail>(PERMISSION_DENIED_EVENT, { detail: { problem, action: options.action } }));
  } catch {
    /* not a problem document */
  }
}
