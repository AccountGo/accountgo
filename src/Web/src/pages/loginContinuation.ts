// SPDX-License-Identifier: AGPL-3.0-only
//
// Post-authentication routing decision, extracted so the signup-continuation resolution is unit-testable
// across every entry path (explicit signup slug, persisted slug, original "from" destination, sign-in with
// no in-memory slug). The decision is resolved from TRUSTED SERVER STATE — the workspace's operating
// policy and the user's org list — not from in-memory signup state that is lost on reload/sign-in, so a
// returning user with an uninitialized signup workspace cannot bypass continuation and reach a dashboard
// with no functional access.

export type PostAuthDestination =
  | { kind: 'continue'; slug: string }
  | { kind: 'dashboard'; slug: string }
  | { kind: 'path'; path: string }
  | { kind: 'select-org' };

export interface PostAuthInputs {
  /** Slug passed explicitly to the post-auth handler (e.g. invite accept target). */
  targetSlugOverride?: string | null;
  /** In-memory slug captured during this signup/sign-in (lost on reload). */
  targetOrgSlug?: string | null;
  /** Slug persisted in storage from a prior visit. */
  persistedSlug?: string | null;
  /** The protected path the user was originally heading to (location.state.from.pathname). */
  fromPath?: string | null;
}

/** Extract an org slug from a protected path like /org/<slug>/... */
export const slugFromPath = (path?: string | null): string | null => {
  if (!path) return null;
  const m = /^\/org\/([^/]+)/.exec(path);
  return m ? decodeURIComponent(m[1]) : null;
};

/**
 * True when the workspace is specifically awaiting the approved signup continuation FOR THIS CALLER, as
 * determined by the server. We route on the server-derived `operatingPolicy.pendingSignupContinuation`
 * flag — computed from the same authoritative, non-mutating predicate the continuation endpoint enforces
 * (signup-specific marker + pristine, uninitialized, sole-owner eligibility) — never on the raw
 * collaboration state. This is decisive: a legacy or otherwise-unclassified workspace that merely looks
 * uninitialized, a non-owner member, a workspace with setup disabled, or an already-initialized workspace
 * all report false, so existing users are never steered into a continuation they cannot complete.
 */
export const workspaceNeedsSetup = async (
  authFetch: (url: string, init?: RequestInit) => Promise<Response>,
  slug: string,
): Promise<boolean> => {
  try {
    const res = await authFetch('/api/organizations/current', { headers: { 'X-AccountGo-Org': slug } });
    if (!res.ok) return false;
    const data = await res.json().catch(() => null);
    return data?.operatingPolicy?.pendingSignupContinuation === true;
  } catch {
    return false;
  }
};

/** When the user has exactly one workspace, return its slug (the candidate pending workspace). */
export const soleWorkspaceSlug = async (
  authFetch: (url: string, init?: RequestInit) => Promise<Response>,
): Promise<string | null> => {
  try {
    const res = await authFetch('/api/organizations');
    if (!res.ok) return null;
    const orgs = (await res.json().catch(() => null)) as Array<{ slug?: string }> | null;
    if (!orgs || orgs.length !== 1 || !orgs[0]?.slug) return null;
    return orgs[0].slug ?? null;
  } catch {
    return null;
  }
};

/**
 * Resolve where to route after authentication. If any candidate workspace (resolved across all entry
 * paths, including the user's sole workspace from the server) still needs setup, route to the guided
 * continuation; otherwise fall back to the normal dashboard / path / selector routing.
 */
export const resolvePostAuthDestination = async (
  authFetch: (url: string, init?: RequestInit) => Promise<Response>,
  inputs: PostAuthInputs,
): Promise<PostAuthDestination> => {
  const { targetSlugOverride, targetOrgSlug, persistedSlug, fromPath } = inputs;

  // CLI consent must win over workspace-setup continuation — do not divert an in-flight OAuth approve.
  if (fromPath && fromPath.startsWith('/cli/authorize')) {
    return { kind: 'path', path: fromPath };
  }

  const candidateSlug =
    targetSlugOverride ||
    targetOrgSlug ||
    persistedSlug ||
    slugFromPath(fromPath) ||
    (await soleWorkspaceSlug(authFetch));

  if (candidateSlug && (await workspaceNeedsSetup(authFetch, candidateSlug))) {
    return { kind: 'continue', slug: candidateSlug };
  }

  if (targetSlugOverride || targetOrgSlug) {
    return { kind: 'dashboard', slug: (targetSlugOverride || targetOrgSlug)! };
  }
  if (fromPath && fromPath !== '/login' && fromPath !== '/signup') {
    return { kind: 'path', path: fromPath };
  }
  const destinationSlug = targetSlugOverride || targetOrgSlug || persistedSlug;
  if (destinationSlug) {
    return { kind: 'dashboard', slug: destinationSlug };
  }
  return { kind: 'select-org' };
};
