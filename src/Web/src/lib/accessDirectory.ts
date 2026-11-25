import { useEffect, useState } from 'react';
import { getOrgSlugFromLocation, useAuth } from '../auth/AuthContext';
import { FULL_WORKSPACE_ACCESS_ROLE_NAME } from './accessPresets';

const BASE = '/api/organizations/current';
const FULL_ACCESS_LABEL = 'Full workspace access';
const CACHE_MS = 60_000;

export interface GrantingRole {
  id: string;
  name: string;
  displayName: string;
  permissionCount: number;
}

export interface AccessDirectory {
  /** Roles from the server's catalog that include the permission, least powerful first. Empty when none or when the catalog could not be read. */
  grantingRoles: GrantingRole[];
  /** Owners and admins by name only. An admin without a name reads "a workspace admin". Never an email. */
  adminNames: string[];
  /** Active members of the workspace. */
  activeMemberCount: number;
  /** The viewer already has a pending request that includes the role chosen for this permission. */
  pendingRequest: boolean;
  /** False when members or the catalog could not be read; the page then shows its heading, sentence and way back only. */
  loaded: boolean;
}

interface RawRole {
  id?: string;
  name?: string;
  displayName?: string;
  permissions?: Array<string | { name?: string }>;
}

interface RawMember {
  userId?: string;
  firstName?: string;
  lastName?: string;
  role?: string;
  status?: string;
}

interface RawRequest {
  status?: string;
  targetUserId?: string;
  requestedRoleIds?: string[];
}

function permissionNames(role: RawRole): string[] {
  return (role.permissions ?? []).map((p) => (typeof p === 'string' ? p : p?.name ?? '')).filter(Boolean);
}

/**
 * The roles that grant a permission, least powerful first (fewest permissions, then by name). "Full workspace access" only
 * leads when it is the only role that grants it.
 */
export function grantingRolesFor(roles: RawRole[], permission: string | string[]): GrantingRole[] {
  const wanted = Array.isArray(permission) ? permission : [permission];
  const granting = roles
    .filter((role) => role.id && permissionNames(role).some((name) => wanted.includes(name)))
    .map((role) => ({
      id: role.id as string,
      name: role.name ?? '',
      displayName: role.name === FULL_WORKSPACE_ACCESS_ROLE_NAME ? FULL_ACCESS_LABEL : role.displayName || role.name || '',
      permissionCount: permissionNames(role).length,
    }));
  const isFull = (role: GrantingRole) => role.name === FULL_WORKSPACE_ACCESS_ROLE_NAME;
  return granting.sort((a, b) => {
    if (isFull(a) !== isFull(b)) return isFull(a) ? 1 : -1;
    return a.permissionCount - b.permissionCount || a.displayName.localeCompare(b.displayName);
  });
}

/** "A", "A or B", "A, B or C". */
export function joinRoleNames(names: string[]): string {
  if (names.length <= 1) return names[0] ?? '';
  return `${names.slice(0, -1).join(', ')} or ${names[names.length - 1]}`;
}

export function adminNamesOf(members: RawMember[]): string[] {
  const names = members
    .filter((m) => m.status === 'Active' && (m.role === 'Owner' || m.role === 'Admin'))
    .map((m) => `${m.firstName ?? ''} ${m.lastName ?? ''}`.trim() || 'a workspace admin');
  return [...new Set(names)];
}

interface Fetched {
  roles: RawRole[];
  members: RawMember[];
  requests: RawRequest[];
}

// Keyed by workspace AND signed-in user: the directory includes that user's own requests, and a different user in the same tab
// must never see another user's.
const cache = new Map<string, { at: number; promise: Promise<Fetched | null> }>();
const cacheKey = (slug: string, userId: string | undefined) => `${slug}\u0000${userId ?? ''}`;

/** After a request is sent, patch the shared entry so every No-access view in the workspace shows it as already sent. */
function recordRequest(slug: string, userId: string | undefined, roleId: string): void {
  const hit = cache.get(cacheKey(slug, userId));
  if (!hit || !userId) return;
  void hit.promise.then((value) => {
    value?.requests.push({ status: 'Pending', targetUserId: userId, requestedRoleIds: [roleId] });
  });
}

/** For tests: forget what was fetched. */
export function clearAccessDirectoryCache(): void {
  cache.clear();
}

/**
 * Members and the role catalog are read once per workspace and shared (a page that shows "No access" several times, or a
 * redraw, does not fetch again). A failure is remembered for the same minute, so there is no retry loop.
 */
export function useAccessDirectory(permission: string | string[] | null | undefined, enabled = true): AccessDirectory & { markRequested: (roleId: string) => void } {
  const { authFetch, user } = useAuth();
  const slug = (typeof window === 'undefined' ? null : getOrgSlugFromLocation()) ?? '';
  const [fetched, setFetched] = useState<Fetched | null | undefined>(undefined);

  useEffect(() => {
    let cancelled = false;
    if (!enabled) return undefined;
    const key = cacheKey(slug, user?.id);
    const hit = cache.get(key);
    let promise: Promise<Fetched | null>;
    if (hit && Date.now() - hit.at < CACHE_MS) {
      promise = hit.promise;
    } else {
      promise = (async () => {
        try {
          const [membersRes, rolesRes, requestsRes] = await Promise.all([
            authFetch(`${BASE}/members`),
            authFetch(`${BASE}/security-roles`),
            authFetch(`${BASE}/access-requests`),
          ]);
          if (!membersRes.ok || !rolesRes.ok) return null;
          return {
            members: (await membersRes.json()) as RawMember[],
            roles: (await rolesRes.json()) as RawRole[],
            requests: requestsRes.ok ? ((await requestsRes.json()) as RawRequest[]) : [],
          };
        } catch {
          return null;
        }
      })();
      cache.set(key, { at: Date.now(), promise });
    }
    void promise.then((value) => {
      if (!cancelled) setFetched(value);
    });
    return () => {
      cancelled = true;
    };
  }, [authFetch, enabled, slug, user?.id]);

  const markRequested = (roleId: string) => {
    recordRequest(slug, user?.id, roleId);
    setFetched((current) => current && { ...current, requests: [...current.requests, { status: 'Pending', targetUserId: user?.id, requestedRoleIds: [roleId] }] });
  };
  if (!fetched) {
    return { grantingRoles: [], adminNames: [], activeMemberCount: 0, pendingRequest: false, loaded: false, markRequested };
  }
  const grantingRoles = permission && permission.length > 0 ? grantingRolesFor(fetched.roles, permission) : [];
  const chosen = grantingRoles[0];
  const pendingRequest = Boolean(
    chosen
      && fetched.requests.some(
        (r) => r.status === 'Pending' && r.targetUserId === user?.id && (r.requestedRoleIds ?? []).includes(chosen.id),
      ),
  );
  return {
    grantingRoles,
    adminNames: adminNamesOf(fetched.members),
    activeMemberCount: fetched.members.filter((m) => m.status === 'Active').length,
    pendingRequest,
    loaded: true,
    markRequested,
  };
}
