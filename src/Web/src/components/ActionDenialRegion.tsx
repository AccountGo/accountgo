import React, { useEffect, useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { ShieldAlert, X } from 'lucide-react';
import { joinRoleNames } from '../lib/accessDirectory';
import type { ClassifiedProblem } from '../lib/apiProblems';
import { PERMISSION_DENIED_EVENT, type PermissionDeniedDetail } from '../lib/permissionDenialEvents';
import { actionPhrase, areaForPermission } from '../lib/permissionLabels';
import { useAccessNextStep } from '../lib/useAccessNextStep';
import type { OrgDetails } from '../types';

export const MAX_ACTION_NOTICES = 3;

interface Notice {
  id: number;
  problem: ClassifiedProblem;
}

let nextId = 1;

/**
 * Where "you can't do that" lands when the server refuses an action the member just tried (a button that was still visible, or one
 * their roles lost mid-session). It is a quiet, polite live region: never a modal, never takes focus, one notice per permission,
 * at most three, each dismissible. It stays until dismissed, the route changes, the workspace changes or the member signs out
 * (the shell unmounts): a timed notice would fail people who read slowly or use a screen reader.
 */
export const ActionDenialRegion: React.FC<{ currentOrg: Pick<OrgDetails, 'id' | 'slug' | 'role'> }> = ({ currentOrg }) => {
  const [notices, setNotices] = useState<Notice[]>([]);
  const location = useLocation();

  useEffect(() => {
    const onDenied = (event: Event) => {
      const detail = (event as CustomEvent<PermissionDeniedDetail>).detail;
      if (!detail?.action) return; // a denied read is explained by its page
      const key = detail.problem.requiredPermission ?? '';
      setNotices((current) => {
        const kept = current.filter((notice) => (notice.problem.requiredPermission ?? '') !== key);
        return [...kept, { id: nextId++, problem: detail.problem }].slice(-MAX_ACTION_NOTICES);
      });
    };
    window.addEventListener(PERMISSION_DENIED_EVENT, onDenied);
    return () => window.removeEventListener(PERMISSION_DENIED_EVENT, onDenied);
  }, []);

  // A new page or workspace starts clean.
  useEffect(() => {
    setNotices([]);
  }, [location.pathname, currentOrg.id]);

  return (
    <div
      role="status"
      aria-live="polite"
      data-testid="action-denial-region"
      // Above the phone bottom bar (4rem) and the home-indicator inset, with a small gap; bottom-left on larger screens.
      className="pointer-events-none fixed left-4 right-4 bottom-[calc(env(safe-area-inset-bottom,0px)+4.5rem)] z-[85] flex flex-col gap-2 md:right-auto md:bottom-4 md:max-w-sm"
    >
      {notices.map((notice) => (
        <ActionDenialNotice
          key={notice.id}
          problem={notice.problem}
          currentOrg={currentOrg}
          onDismiss={() => setNotices((current) => current.filter((candidate) => candidate.id !== notice.id))}
        />
      ))}
    </div>
  );
};

const ActionDenialNotice: React.FC<{
  problem: ClassifiedProblem;
  currentOrg: Pick<OrgDetails, 'slug' | 'role'>;
  onDismiss: () => void;
}> = ({ problem, currentOrg, onDismiss }) => {
  const permission = problem.requiredPermission;
  const phrase = actionPhrase(permission);
  const area = areaForPermission(permission) ?? 'this';
  const { roleNames, adminList, adminAction, canAsk, requested, requestState, askAdmin } = useAccessNextStep(permission, currentOrg, area);

  const sentence = roleNames.length > 0 && phrase
    // "Full workspace access" is itself a role name, so "...access to" would read "access access"; the action leads instead.
    ? `To ${phrase} you need ${joinRoleNames(roleNames)}.`
    : phrase
      ? `You don't have access to ${phrase}.`
      : "You don't have access to do that.";

  const buttonClass = 'inline-flex items-center justify-center rounded-lg px-3 py-1.5 text-xs font-semibold';
  return (
    <div
      data-testid="action-denial-notice"
      className="pointer-events-auto rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-4 shadow-xl text-xs text-[var(--text-secondary)]"
    >
      <div className="flex items-start gap-3">
        <ShieldAlert className="mt-0.5 h-4 w-4 shrink-0 text-amber-500" aria-hidden="true" />
        <div className="min-w-0 flex-1 space-y-2 leading-relaxed">
          <p className="text-[var(--text-main)]">{sentence}</p>
          {adminAction && (
            <Link to={adminAction.to} className={`${buttonClass} bg-[var(--brand-primary)] text-white`}>
              {adminAction.label}
            </Link>
          )}
          {canAsk && (
            <button
              type="button"
              className={`${buttonClass} bg-[var(--brand-primary)] text-white disabled:opacity-60`}
              disabled={requestState === 'sending' || requested}
              onClick={() => void askAdmin()}
            >
              {requestState === 'sending' ? 'Sending…' : 'Ask an admin for access'}
            </button>
          )}
          {!currentOrgIsAdmin(currentOrg) && requested && (
            <p className="text-emerald-600 dark:text-emerald-400">
              {requestState === 'sent' ? 'Request sent' : 'Request already sent'}
              {adminList ? ` to ${adminList}` : ''}.
            </p>
          )}
          {!currentOrgIsAdmin(currentOrg) && requestState === 'failed' && (
            <p className="text-rose-600 dark:text-rose-400">The request couldn’t be sent. Try again, or ask your admin directly.</p>
          )}
          <details className="text-[var(--text-muted)]">
            <summary className="cursor-pointer select-none">Details</summary>
            <dl className="mt-1 space-y-0.5">
              <div><dt className="inline font-medium">Code: </dt><dd className="inline">{problem.code ?? 'PERMISSION_DENIED'}</dd></div>
              {permission && <div><dt className="inline font-medium">Permission: </dt><dd className="inline">{permission}</dd></div>}
              {problem.requestId && <div><dt className="inline font-medium">Request ID: </dt><dd className="inline">{problem.requestId}</dd></div>}
            </dl>
          </details>
        </div>
        <button
          type="button"
          aria-label="Dismiss this notice"
          className="shrink-0 rounded p-1 text-[var(--text-muted)] hover:bg-[var(--bg-subtle)]"
          onClick={onDismiss}
        >
          <X className="h-4 w-4" aria-hidden="true" />
        </button>
      </div>
    </div>
  );
};

const currentOrgIsAdmin = (org: { role?: string }) => org.role === 'Owner' || org.role === 'Admin';
