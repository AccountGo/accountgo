import React, { useEffect, useRef, useState } from 'react';
import { Link, useOutletContext } from 'react-router-dom';
import { ShieldAlert } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';
import { joinRoleNames, useAccessDirectory } from '../lib/accessDirectory';
import type { ClassifiedProblem } from '../lib/apiProblems';
import { areaForPermission } from '../lib/permissionLabels';
import type { TenantContextType } from '../types';

const BASE = '/api/organizations/current';
type RequestState = 'idle' | 'sending' | 'sent' | 'failed';

/**
 * Shown in place of a page that could not load because the viewer's roles do not include it. It says what is locked, which roles
 * open it and what to do next. It never shows data, and never says whether a record exists (a missing record is a plain "not found").
 */
export const NoAccess: React.FC<{ problem: ClassifiedProblem; headingLevel?: 1 | 2; area?: string }> = ({ problem, headingLevel = 2, area: areaName }) => {
  const { currentOrg } = useOutletContext<TenantContextType>();
  const { authFetch } = useAuth();
  const headingRef = useRef<HTMLHeadingElement>(null);
  // Pages keep their own title as the h1, so inside a page this is an h2; only a full-page use passes 1.
  const Heading = headingLevel === 1 ? 'h1' : 'h2';
  // A ref, not state: two clicks before the next render must still send one request.
  const sentRef = useRef(false);
  const [requestState, setRequestState] = useState<RequestState>('idle');
  const directory = useAccessDirectory(problem.requiredAnyOf ?? problem.requiredPermission);

  const area = areaName ?? areaForPermission(problem.requiredPermission) ?? 'this page';
  const role = currentOrg.role;
  const isAdmin = role === 'Owner' || role === 'Admin';
  const slug = encodeURIComponent(currentOrg.slug);
  const chosen = directory.grantingRoles[0];
  const grant = chosen ? `&grant=${encodeURIComponent(chosen.name)}` : '';
  const roleNames = directory.grantingRoles.map((r) => r.displayName);
  const adminList = directory.adminNames.length > 0 ? joinRoleNames(directory.adminNames) : null;
  const requested = requestState === 'sent' || directory.pendingRequest;

  useEffect(() => {
    headingRef.current?.focus();
  }, []);

  const askAdmin = async () => {
    if (!chosen || sentRef.current || requestState === 'sending' || requested) return;
    sentRef.current = true;
    setRequestState('sending');
    try {
      const res = await authFetch(`${BASE}/access-requests`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        // The workspace's own request flow decides: the admin can approve it, change it or decline it.
        body: JSON.stringify({ securityRoleIds: [chosen.id], reason: `Needs access to ${area}` }),
      });
      sentRef.current = res.ok;
      if (res.ok) directory.markRequested(chosen.id);
      setRequestState(res.ok ? 'sent' : 'failed');
    } catch {
      sentRef.current = false;
      setRequestState('failed');
    }
  };

  const linkClass = 'inline-flex items-center justify-center rounded-xl px-4 py-2 text-sm font-semibold';
  const primaryLink = `${linkClass} bg-[var(--brand-primary)] text-white`;
  const secondaryLink = `${linkClass} border border-[var(--border-subtle)] text-[var(--text-main)] hover:bg-[var(--bg-subtle)]`;

  return (
    <section
      data-testid="no-access"
      aria-labelledby="no-access-heading"
      className="w-full max-w-lg mx-auto my-10 p-6 sm:p-8 rounded-3xl bg-[var(--bg-surface)] shadow-xl space-y-4"
    >
      <div className="w-12 h-12 rounded-2xl bg-amber-500/10 text-amber-500 flex items-center justify-center" aria-hidden="true">
        <ShieldAlert className="w-6 h-6" />
      </div>
      <Heading id="no-access-heading" ref={headingRef} tabIndex={-1} className="text-xl font-bold text-[var(--text-main)] focus:outline-none">
        {`You don't have access to ${area}`}
      </Heading>
      <p className="text-sm text-[var(--text-secondary)] leading-relaxed">Your current access doesn’t include this part of the workspace.</p>
      {roleNames.length > 0 && (
        <p className="text-sm text-[var(--text-secondary)] leading-relaxed">
          Access comes with these roles: <span className="font-medium text-[var(--text-main)]">{joinRoleNames(roleNames)}</span>.
        </p>
      )}

      {directory.loaded && isAdmin && (
        <p className="text-sm text-[var(--text-secondary)]">
          {directory.activeMemberCount <= 1 ? 'You can give yourself the access you need.' : 'You can request the roles you need.'}
        </p>
      )}
      {directory.loaded && !isAdmin && adminList && (
        <p className="text-sm text-[var(--text-secondary)]">Ask {adminList} for access.</p>
      )}

      <div className="flex flex-wrap gap-3 pt-1">
        {directory.loaded && isAdmin && (
          <Link
            className={primaryLink}
            to={`/org/${slug}/members?access=${directory.activeMemberCount <= 1 ? 'setup' : 'request'}${grant}`}
          >
            {directory.activeMemberCount <= 1 ? 'Set up my access' : 'Request functional roles'}
          </Link>
        )}
        {directory.loaded && !isAdmin && chosen && (
          <button type="button" className={`${primaryLink} disabled:opacity-60`} disabled={requestState === 'sending' || requested} onClick={() => void askAdmin()}>
            {requestState === 'sending' ? 'Sending…' : 'Ask an admin for access'}
          </button>
        )}
        <Link className={secondaryLink} to={`/org/${slug}/dashboard`}>
          Go to dashboard
        </Link>
      </div>

      {!isAdmin && requested && (
        <p role="status" className="text-sm text-emerald-600 dark:text-emerald-400">
          {requestState === 'sent' ? 'Request sent' : 'Request already sent'}
          {adminList ? ` to ${adminList}` : ''}. You’ll get access once an admin approves it.
        </p>
      )}
      {!isAdmin && requestState === 'failed' && (
        <p role="status" className="text-sm text-rose-600 dark:text-rose-400">The request couldn’t be sent. Try again, or ask your admin directly.</p>
      )}

      <details className="text-xs text-[var(--text-muted)]">
        <summary className="cursor-pointer select-none">Details</summary>
        <dl className="mt-2 space-y-1">
          <div><dt className="inline font-medium">Code: </dt><dd className="inline">{problem.code ?? 'PERMISSION_DENIED'}</dd></div>
          {problem.requiredPermission && (
            <div><dt className="inline font-medium">Permission: </dt><dd className="inline">{problem.requiredPermission}</dd></div>
          )}
          {problem.requestId && (
            <div><dt className="inline font-medium">Request ID: </dt><dd className="inline">{problem.requestId}</dd></div>
          )}
        </dl>
      </details>
    </section>
  );
};

/** Renders the No-access view when a load was denied, the page's own body otherwise. */
export const NoAccessOr: React.FC<{ denied: ClassifiedProblem | null; children: React.ReactNode }> = ({ denied, children }) =>
  denied ? <NoAccess problem={denied} /> : <>{children}</>;
