import type { ClassifiedProblem } from './apiProblems';
import { PERMISSION_DENIED_EVENT, type PermissionDeniedDetail } from './permissionDenialEvents';
import { SESSION_CLEARED_EVENT } from './workspaceAccessEvents';

/**
 * The notices for actions the server refused. They live outside React on purpose: a permission refresh changes the workspace's
 * permissions, which remounts parts of the workspace shell (its providers are keyed by them), and a notice must survive that.
 */
export const MAX_ACTION_NOTICES = 3;

export interface ActionNotice {
  id: number;
  problem: ClassifiedProblem;
}

let notices: ActionNotice[] = [];
let nextId = 1;
let context: string | null = null;
const listeners = new Set<() => void>();

const emit = () => listeners.forEach((listener) => listener());

function add(problem: ClassifiedProblem): void {
  const key = problem.requiredPermission ?? '';
  // One notice per permission: a repeat replaces the earlier one. At most three; the oldest goes.
  const kept = notices.filter((notice) => (notice.problem.requiredPermission ?? '') !== key);
  notices = [...kept, { id: nextId++, problem }].slice(-MAX_ACTION_NOTICES);
  emit();
}

if (typeof window !== 'undefined') {
  window.addEventListener(PERMISSION_DENIED_EVENT, (event) => {
    const detail = (event as CustomEvent<PermissionDeniedDetail>).detail;
    if (detail?.action) add(detail.problem); // a denied read is explained by its page
  });
  window.addEventListener(SESSION_CLEARED_EVENT, () => actionNotices.clear());
}

export const actionNotices = {
  subscribe(listener: () => void): () => void {
    listeners.add(listener);
    return () => listeners.delete(listener);
  },
  getSnapshot: (): ActionNotice[] => notices,
  dismiss(id: number): void {
    notices = notices.filter((notice) => notice.id !== id);
    emit();
  },
  clear(): void {
    if (notices.length === 0) return;
    notices = [];
    emit();
  },
  /**
   * Called by the region on every render context (user, workspace, route). Notices clear when that context changes, not when the
   * region merely remounts inside the same one.
   */
  enter(nextContext: string): void {
    if (context === nextContext) return;
    context = nextContext;
    actionNotices.clear();
  },
  /** For tests: forget everything, as in a fresh page load. */
  reset(): void {
    notices = [];
    context = null;
    emit();
  },
};
