import React from 'react';
import { useMatches } from 'react-router-dom';
import { Loader2 } from 'lucide-react';
import { NoAccess } from './NoAccess';
import { useTenantContext } from './Gating';
import { accessKeys, isPublicAccess, type OrgRouteHandle } from '../lib/routeAccess';

/**
 * Stops a page from mounting (and so from fetching) when the route's declared permission is missing, and shows the "No access"
 * page as the whole page body instead. It reads the route's own `handle.access`, so the declaration lives in one place.
 * Put it inside the module gate: a plan or module problem is explained by that gate first.
 */
export const RouteGuard: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const { userPermissions, hasPermission } = useTenantContext();
  const matches = useMatches();
  const access = (matches[matches.length - 1]?.handle as OrgRouteHandle | undefined)?.access;

  // A route without a declaration is not guarded here; the architecture test makes that impossible for org routes.
  if (!access || isPublicAccess(access)) return <>{children}</>;

  // The layout only renders routes once the workspace and its permissions have loaded. An empty list is a loaded answer (a member
  // with no roles) and is denied; only a missing list counts as still loading.
  if (userPermissions === undefined) {
    return (
      <div className="py-24 flex items-center justify-center text-[var(--text-secondary)]" role="status">
        <Loader2 className="w-6 h-6 animate-spin" aria-label="Loading" />
      </div>
    );
  }

  const keys = accessKeys(access);
  if (hasPermission(keys)) return <>{children}</>;

  return (
    <NoAccess
      headingLevel={1}
      area={access.label}
      problem={{ category: 'permission-denied', status: 403, code: 'PERMISSION_DENIED', requiredPermission: keys[0], requiredAnyOf: keys }}
    />
  );
};
