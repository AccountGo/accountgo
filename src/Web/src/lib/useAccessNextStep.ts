import { useRef, useState } from 'react';
import { useAuth } from '../auth/AuthContext';
import { joinRoleNames, useAccessDirectory } from './accessDirectory';

const BASE = '/api/organizations/current';
export type RequestState = 'idle' | 'sending' | 'sent' | 'failed';

/**
 * What a member can do about a permission they lack, from the live workspace directory: the roles that include it, who to ask, and
 * the right next step for their situation (set up own access when working alone, request roles in a team, or ask an admin). Shared
 * by the full-page No-access view and the action notice so the two never drift apart.
 */
export function useAccessNextStep(
  permission: string | string[] | null | undefined,
  org: { slug: string; role?: string },
  /** Names what the member needs, for the request's reason text. */
  reasonArea: string,
) {
  const { authFetch } = useAuth();
  const directory = useAccessDirectory(permission);
  // A ref, not state: two clicks before the next render must still send one request.
  const sentRef = useRef(false);
  const [requestState, setRequestState] = useState<RequestState>('idle');

  const isAdmin = org.role === 'Owner' || org.role === 'Admin';
  const slug = encodeURIComponent(org.slug);
  const chosen = directory.grantingRoles[0];
  const grant = chosen ? `&grant=${encodeURIComponent(chosen.name)}` : '';
  const roleNames = directory.grantingRoles.map((r) => r.displayName);
  const adminList = directory.adminNames.length > 0 ? joinRoleNames(directory.adminNames) : null;
  const requested = requestState === 'sent' || directory.pendingRequest;
  const solo = directory.activeMemberCount <= 1;

  const askAdmin = async () => {
    if (!chosen || sentRef.current || requestState === 'sending' || requested) return;
    sentRef.current = true;
    setRequestState('sending');
    try {
      const res = await authFetch(`${BASE}/access-requests`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        // The workspace's own request flow decides: the admin can approve it, change it or decline it.
        body: JSON.stringify({ securityRoleIds: [chosen.id], reason: `Needs access to ${reasonArea}` }),
      });
      sentRef.current = res.ok;
      if (res.ok) directory.markRequested(chosen.id);
      setRequestState(res.ok ? 'sent' : 'failed');
    } catch {
      sentRef.current = false;
      setRequestState('failed');
    }
  };

  return {
    directory,
    isAdmin,
    slug,
    chosen,
    roleNames,
    adminList,
    requested,
    requestState,
    solo,
    askAdmin,
    /** Link target and label for an owner or admin; null until the directory has loaded or for a plain member. */
    adminAction: directory.loaded && isAdmin
      ? {
          to: `/org/${slug}/members?access=${solo ? 'setup' : 'request'}${grant}`,
          label: solo ? 'Set up my access' : 'Request functional roles',
          hint: solo ? 'You can give yourself the access you need.' : 'You can request the roles you need.',
        }
      : null,
    /** Whether a plain member can send the one-click request. */
    canAsk: directory.loaded && !isAdmin && Boolean(chosen),
  };
}
