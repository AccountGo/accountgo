/**
 * What an organization route needs from the signed-in member, declared once on the route (`handle.access`) so the client can
 * stop asking for pages it already knows are forbidden. The API stays the authority: this only avoids the doomed request and
 * explains why. A test fails if an org route declares none of these, or names a permission the server does not define.
 */
export type RouteAccess =
  | { permission: string; label?: string }
  | { anyOf: readonly string[]; label?: string }
  | { publicInWorkspace: string };

export interface OrgRouteHandle {
  access: RouteAccess;
}

/** The member needs this permission (the one the page's first, primary load is denied without). `label` is the page's name. */
export const requires = (permission: string, label?: string): OrgRouteHandle => ({ access: { permission, label } });

/** The member needs at least one of these permissions. */
export const requiresAny = (anyOf: readonly string[], label?: string): OrgRouteHandle => ({ access: { anyOf, label } });

/** Every member of the workspace may open it. The reason says why, and the architecture test requires one. */
export const publicInWorkspace = (reason: string): OrgRouteHandle => ({ access: { publicInWorkspace: reason } });

export function isPublicAccess(access: RouteAccess): access is { publicInWorkspace: string } {
  return 'publicInWorkspace' in access;
}

/** The permissions a non-public declaration names, in the order declared. */
export function accessKeys(access: RouteAccess): string[] {
  if (isPublicAccess(access)) return [];
  return 'permission' in access ? [access.permission] : [...access.anyOf];
}
