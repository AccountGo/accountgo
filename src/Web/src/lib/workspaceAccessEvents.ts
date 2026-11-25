import { classifyResponse } from './apiProblems';

/** A call made for a workspace answered "that workspace is not yours (any more)". Carries no response content. */
export const WORKSPACE_ACCESS_LOST_EVENT = 'accountgo:workspace-access-lost';

/** Fired only for responses to org-scoped requests (the caller passes the org header it sent). */
export async function announceWorkspaceAccessLost(response: Response, orgSlug: string | null): Promise<void> {
  if (!orgSlug || response.status !== 404 || typeof window === 'undefined') return;
  const problem = await classifyResponse(response);
  if (problem?.category !== 'workspace-access-lost') return;
  window.dispatchEvent(new CustomEvent(WORKSPACE_ACCESS_LOST_EVENT, { detail: { slug: orgSlug } }));
}

/** Fired when the session ends for any reason, or a workspace is left, so caches tied to them (action notices, member lists, permission refresh timers) forget what they hold. */
export const SESSION_CLEARED_EVENT = 'accountgo:session-cleared';
