import { useEffect } from 'react';
import { PERMISSION_DENIED_EVENT } from './permissionDenialEvents';

/**
 * A server permission denial (an action, or a call a page made) means the permissions on screen may be stale. Re-reads them once
 * through the workspace's shared refresh, unforced: its one-minute limit and in-flight sharing apply, so two denials in a row make one
 * read and nothing loops. A control the member no longer holds then disappears on its own.
 */
export function useActionDenialRefresh(refreshPermissions: (options?: { force?: boolean }) => Promise<boolean>): void {
  useEffect(() => {
    const onDenied = () => {
      void refreshPermissions();
    };
    window.addEventListener(PERMISSION_DENIED_EVENT, onDenied);
    return () => window.removeEventListener(PERMISSION_DENIED_EVENT, onDenied);
  }, [refreshPermissions]);
}
