import { useCallback, useEffect, useRef } from 'react';
import { SESSION_CLEARED_EVENT, WORKSPACE_ACCESS_LOST_EVENT } from './workspaceAccessEvents';

/**
 * The one way out of a workspace the caller no longer has. `leave()` runs once per workspace (many parallel calls can fail
 * together): it forgets what was learned for the workspace (action notices, member lists, permission refresh timers), then
 * runs `onLeave` (clear the shell, go to /select-org). It also runs when any call made for this workspace answers
 * "workspace not found" (see announceWorkspaceAccessLost).
 */
export function useWorkspaceLoss(orgSlug: string | undefined, onLeave: () => void): () => void {
  const leftRef = useRef(false);
  const onLeaveRef = useRef(onLeave);
  useEffect(() => { onLeaveRef.current = onLeave; }, [onLeave]);
  useEffect(() => { leftRef.current = false; }, [orgSlug]);

  const leave = useCallback(() => {
    if (leftRef.current) return;
    leftRef.current = true;
    window.dispatchEvent(new Event(SESSION_CLEARED_EVENT));
    onLeaveRef.current();
  }, []);

  useEffect(() => {
    const onLost = (event: Event) => {
      const slug = (event as CustomEvent<{ slug?: string }>).detail?.slug;
      if (slug && orgSlug && slug.toLowerCase() === orgSlug.toLowerCase()) leave();
    };
    window.addEventListener(WORKSPACE_ACCESS_LOST_EVENT, onLost);
    return () => window.removeEventListener(WORKSPACE_ACCESS_LOST_EVENT, onLost);
  }, [orgSlug, leave]);

  return leave;
}
