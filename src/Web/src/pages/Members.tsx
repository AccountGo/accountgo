import React, { useCallback, useEffect, useState } from 'react';
import { useNavigate, useOutletContext } from 'react-router-dom';
import { RefreshCw, UserPlus, ShieldCheck, ShieldAlert, RotateCcw } from 'lucide-react';
import {
  AccessSetupStatus,
  RoleItem,
  SoloSetupEligibility,
  TenantContextType,
  WorkspaceAccessRequestItem,
  WorkspaceInvitation,
  WorkspaceMember,
  WorkspaceOperatingPolicySummary,
  WorkspaceRole,
} from '../types';
import { useAuth } from '../auth/AuthContext';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { PageHeader, HeaderButton } from '../components/PageHeader';
import { useCapabilities } from '../lib/entitlements';

interface PendingConfirm {
  title: string;
  message: string;
  confirmLabel: string;
  destructive: boolean;
  action: () => Promise<void>;
}

const base = '/api/organizations/current';
const button = 'px-3 py-2 rounded-lg border border-[var(--border-subtle)] hover:bg-[var(--bg-subtle)] disabled:opacity-50 disabled:cursor-not-allowed';
const input = 'w-full p-2 rounded-lg bg-[var(--bg-base)] border border-[var(--border-subtle)]';

async function requireSuccess(response: Response): Promise<void> {
  if (response.ok) return;
  const text = await response.text();
  let message = text || `Request failed (${response.status})`;
  try {
    const problem: { detail?: string; title?: string } = JSON.parse(text);
    message = problem.detail || problem.title || message;
  } catch {
    // Non-JSON proxy responses are displayed as text.
  }
  if (response.status === 409) {
    throw new Error(`${message} Review the latest workspace setup and try again.`);
  }
  throw new Error(message);
}

function journalWorkflowLabel(mode?: string): string {
  switch (mode) {
    case 'SingleActorAllowed':
      return 'You can prepare and post your own journals under the single-user workflow.';
    case 'IndependentReviewerRequired':
      return 'Independent review is required before posting journals.';
    default:
      return 'Journal posting follows the workspace approval policy.';
  }
}

export const Members: React.FC = () => {
  const { currentOrg, updateCurrentOrg } = useOutletContext<TenantContextType>();
  const { authFetch, user } = useAuth();
  const navigate = useNavigate();
  const [members, setMembers] = useState<WorkspaceMember[]>([]);
  const [invitations, setInvitations] = useState<WorkspaceInvitation[]>([]);
  const [accessRequests, setAccessRequests] = useState<WorkspaceAccessRequestItem[]>([]);
  const [setupStatus, setSetupStatus] = useState<AccessSetupStatus | null>(null);
  const [operatingSettings, setOperatingSettings] = useState<WorkspaceOperatingPolicySummary | null>(null);
  const [soloEligibility, setSoloEligibility] = useState<SoloSetupEligibility | null>(null);
  const [roles, setRoles] = useState<RoleItem[]>([]);
  const [currentRole, setCurrentRole] = useState<WorkspaceRole>('Member');
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [query, setQuery] = useState('');
  const [form, setForm] = useState<'invite' | 'request' | 'setup' | WorkspaceMember | null>(null);
  const [email, setEmail] = useState('');
  const [role, setRole] = useState<'Admin' | 'Member'>('Member');
  const [roleIds, setRoleIds] = useState<number[]>([]);
  const [requestTargetMemberId, setRequestTargetMemberId] = useState<number | null>(null);
  const [requestReason, setRequestReason] = useState('');
  const [pendingConfirm, setPendingConfirm] = useState<PendingConfirm | null>(null);
  const { canCreate: canCreateResource } = useCapabilities();
  const canManage = currentRole === 'Owner' || currentRole === 'Admin';
  // Seats have no module — invite UI is permission ∧ canCreate(seats).
  const canInvite = canManage && canCreateResource('seats');
  const isOwner = currentRole === 'Owner';

  const isEligibleForSolo = isOwner && (soloEligibility?.status === 'Eligible' || soloEligibility?.status === 0);
  const isRecoveryRequired = isOwner && (soloEligibility?.status === 'RecoveryRequired' || soloEligibility?.status === 2);
  const isSoloActive = operatingSettings?.collaborationState === 'SoloActive';
  const canReturnToSolo = Boolean(
    isOwner &&
    !isSoloActive &&
    operatingSettings?.soloSetupEnabled &&
    operatingSettings?.allowedNextActions?.includes('return_to_solo')
  );

  const load = useCallback(async (signal?: AbortSignal) => {
    setLoading(true);
    setError('');
    setMembers([]);
    setInvitations([]);
    setAccessRequests([]);
    setSetupStatus(null);
    setOperatingSettings(null);
    setSoloEligibility(null);
    setRoles([]);
    setCurrentRole('Member');
    try {
      const current = await authFetch(base, { signal });
      await requireSuccess(current);
      const workspace: { role: WorkspaceRole } = await current.json();
      setCurrentRole(workspace.role);

      const isOwnerRole = workspace.role === 'Owner';
      const coreRequests: Promise<Response>[] = [
        authFetch(`${base}/members`, { signal }),
        authFetch(`${base}/security-roles`, { signal }),
        authFetch(`${base}/access-requests`, { signal }),
      ];
      if (workspace.role !== 'Member') {
        coreRequests.push(authFetch(`${base}/invitations`, { signal }));
      }
      if (isOwnerRole) {
        coreRequests.push(authFetch(`${base}/access-setup/status`, { signal }));
      }

      const coreResponses = await Promise.all(coreRequests);
      await Promise.all(coreResponses.map(requireSuccess));

      const memberRows = await coreResponses[0].json();
      const roleRows = await coreResponses[1].json();
      const requestRows = await coreResponses[2].json();

      let inviteRows: WorkspaceInvitation[] = [];
      let setupInfo: AccessSetupStatus | null = null;
      let nextIndex = 3;
      if (workspace.role !== 'Member') {
        inviteRows = await coreResponses[nextIndex].json();
        nextIndex++;
      }
      if (isOwnerRole) {
        setupInfo = await coreResponses[nextIndex].json();
        nextIndex++;
      }

      let operatingInfo: WorkspaceOperatingPolicySummary | null = null;
      let soloInfo: SoloSetupEligibility | null = null;
      try {
        const opRes = await authFetch(`${base}/operating-settings`, { signal });
        if (opRes.ok) {
          const parsed = await opRes.json();
          if (parsed && !Array.isArray(parsed)) operatingInfo = parsed;
        }
      } catch {
        // non-blocking
      }

      if (isOwnerRole) {
        try {
          const soloRes = await authFetch(`${base}/solo-setup/eligibility`, { signal });
          if (soloRes.ok) {
            const parsed = await soloRes.json();
            if (parsed && !Array.isArray(parsed)) soloInfo = parsed;
          }
        } catch {
          // non-blocking
        }
      }

      if (signal?.aborted) return;
      setMembers(memberRows);
      setRoles(roleRows);
      setAccessRequests(requestRows || []);
      setInvitations(inviteRows || []);
      setSetupStatus(setupInfo);
      setOperatingSettings(operatingInfo);
      setSoloEligibility(soloInfo);
    } catch (err) {
      if (signal?.aborted) return;
      setError(err instanceof Error ? err.message : 'Could not load workspace members.');
    } finally {
      if (!signal?.aborted) setLoading(false);
    }
  }, [authFetch, currentOrg.slug]);

  useEffect(() => {
    const controller = new AbortController();
    setForm(null);
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const mutate = async (path: string, method: string, body: object | undefined, success: string, leaving = false) => {
    setBusy(true);
    setError('');
    setNotice('');
    try {
      const response = await authFetch(`${base}/${path}`, {
        method,
        ...(body ? { headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) } : {}),
      });
      await requireSuccess(response);
      if (path === 'owner-access-setup') {
        const current = await authFetch(base);
        if (current.ok) {
          const updated = await current.json();
          if (Array.isArray(updated.permissions)) updateCurrentOrg?.({ permissions: updated.permissions });
        }
      }
      setForm(null);
      setNotice(success);
      if (leaving) navigate('/select-org', { replace: true });
      else await load();
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Workspace change failed.';
      setError(message);
      if (message.includes('Review the latest workspace setup')) {
        void load();
      }
    } finally {
      setBusy(false);
    }
  };

  const openForm = (target: 'invite' | 'request' | 'setup' | WorkspaceMember) => {
    setForm(target);
    setError('');
    if (target === 'invite') {
      setEmail('');
      setRole('Member');
      setRoleIds([]);
    } else if (target === 'request') {
      setRequestTargetMemberId(null);
      setRequestReason('');
      setRoleIds([]);
    } else if (target === 'setup') {
      setRoleIds([]);
    } else {
      setEmail(target.email);
      setRole(target.role === 'Owner' ? 'Member' : target.role);
      setRoleIds(target.securityRoles.map(r => r.id));
    }
  };

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    if (!form) return;
    if (form === 'invite') {
      void mutate('invitations', 'POST', { email: email.trim(), role, securityRoleIds: roleIds }, 'Invitation email sent.');
    } else if (form === 'request') {
      void mutate('access-requests', 'POST', {
        targetMemberId: requestTargetMemberId,
        securityRoleIds: roleIds,
        reason: requestReason.trim(),
      }, 'Access request submitted for independent approval.');
    } else if (form === 'setup') {
      void mutate('owner-access-setup', 'POST', {
        securityRoleIds: roleIds,
      }, 'Your functional roles have been updated.');
    } else {
      void mutate(`members/${form.memberId}`, 'PATCH', {
        ...(form.role !== 'Owner' ? { role } : {}),
        securityRoleIds: roleIds,
      }, 'Workspace membership updated.');
    }
  };

  const submitAsAccessRequest = () => {
    if (!form || typeof form !== 'object') return;
    void mutate('access-requests', 'POST', {
      targetMemberId: form.memberId,
      securityRoleIds: roleIds,
      reason: `Delegation request for ${form.email}`,
    }, 'Access request submitted for independent approval.');
  };

  const confirmSoloSetup = () => {
    if (!soloEligibility) return;
    setPendingConfirm({
      title: 'Confirm solo workspace setup?',
      message:
        'This grants you Full Workspace Access as the owner and configures the single-actor journal workflow. Existing approval rules remain governed by workspace policy.',
      confirmLabel: 'Confirm solo setup',
      destructive: false,
      action: () =>
        mutate(
          'solo-setup/confirm',
          'POST',
          { expectedPolicyRevision: soloEligibility.policyRevision },
          'Solo workspace setup confirmed successfully.'
        ),
    });
  };

  const returnToSolo = () => {
    if (!operatingSettings) return;
    setPendingConfirm({
      title: 'Return workspace to just me?',
      message:
        'This will return the workspace to a solo operating state and revoke outstanding invitations. Existing access grants and approval rules are preserved.',
      confirmLabel: 'Return to just me',
      destructive: false,
      action: () =>
        mutate(
          'operating-settings/return-to-solo',
          'POST',
          {
            expectedPolicyRevision: operatingSettings.policyRevision ?? 0,
            revokeOutstandingInvitations: true,
          },
          'Workspace returned to solo operating state.'
        ),
    });
  };

  const visibleMembers = members.filter(m =>
    `${m.firstName} ${m.lastName} ${m.email} ${m.role}`.toLowerCase().includes(query.toLowerCase()));

  return (
    <div className="space-y-6 text-[var(--text-main)]">
      <PageHeader
        title="Team & Members"
        subtitle={`${currentOrg.name} · Your workspace role: ${currentRole}`}
        actions={
          <>
            <HeaderButton label="Refresh" icon={RefreshCw} iconOnly busy={loading} disabled={busy} onClick={() => void load()} />
            {isEligibleForSolo && (
              <HeaderButton
                label="Confirm solo setup"
                icon={ShieldCheck}
                variant="primary"
                disabled={busy || loading}
                onClick={confirmSoloSetup}
              />
            )}
            {setupStatus?.ownerCanManageOwnAccess && !isEligibleForSolo && (
              <HeaderButton
                label="Set up my access"
                icon={ShieldAlert}
                variant="primary"
                disabled={busy || loading}
                onClick={() => openForm('setup')}
              />
            )}
            {canReturnToSolo && (
              <HeaderButton
                label="Return to just me"
                icon={RotateCcw}
                disabled={busy || loading}
                onClick={returnToSolo}
              />
            )}
            {(!isSoloActive || members.length > 1) && (
              <HeaderButton
                label="Request functional roles"
                icon={ShieldCheck}
                disabled={busy || loading}
                onClick={() => openForm('request')}
              />
            )}
            {canInvite && (
              <HeaderButton label="Invite member" icon={UserPlus} variant="primary" disabled={busy || loading} onClick={() => openForm('invite')} />
            )}
          </>
        }
      />
      {error && <div role="alert" className="p-4 rounded-xl bg-red-500/10 text-red-400">{error}</div>}
      {notice && <div role="status" className="p-4 rounded-xl bg-emerald-500/10 text-emerald-400">{notice}</div>}

      {/* Your access summary */}
      {(operatingSettings?.ownAccessProfileDisplayName || isSoloActive) && (
        <div className="p-4 rounded-xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] space-y-1">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h3 className="text-sm font-semibold text-[var(--text-main)]">Your access summary</h3>
            {isSoloActive && (
              <span className="text-xs px-2 py-0.5 rounded-full bg-emerald-500/20 text-emerald-400 font-medium">
                Solo workspace (just me)
              </span>
            )}
          </div>
          <p className="text-sm text-[var(--text-muted)]">
            <span className="font-medium text-[var(--text-main)]">{currentRole}</span>
            {operatingSettings?.ownAccessProfileDisplayName && (
              <> · <span className="font-medium text-[var(--text-main)]">{operatingSettings.ownAccessProfileDisplayName}</span></>
            )}
          </p>
          <p className="text-xs text-[var(--text-muted)]">
            {journalWorkflowLabel(operatingSettings?.journalWorkflow)}
          </p>
        </div>
      )}

      {/* Eligible for Solo Setup Banner */}
      {isEligibleForSolo && soloEligibility && (
        <div className="p-5 rounded-xl border border-emerald-500/30 bg-emerald-500/10 flex flex-col md:flex-row items-start md:items-center justify-between gap-4">
          <div>
            <h2 className="font-semibold text-emerald-400">Solo Workspace Setup Available</h2>
            <p className="text-sm text-[var(--text-muted)]">
              {soloEligibility.message || 'This workspace appears to have always been solo.'} Confirming solo setup grants you full workspace access and activates single-actor journal posting without requiring independent review.
            </p>
          </div>
          <button
            type="button"
            className="px-4 py-2 rounded-lg bg-emerald-600 hover:bg-emerald-500 text-white font-medium whitespace-nowrap"
            onClick={confirmSoloSetup}
            disabled={busy}
          >
            Confirm solo setup
          </button>
        </div>
      )}

      {/* Controlled Recovery Required Banner */}
      {isRecoveryRequired && soloEligibility && (
        <div className="p-5 rounded-xl border border-amber-500/30 bg-amber-500/10 flex flex-col items-start gap-2">
          <div className="flex items-center gap-2">
            <ShieldAlert className="w-5 h-5 text-amber-400" />
            <h2 className="font-semibold text-amber-400">Controlled Recovery Required</h2>
          </div>
          <p className="text-sm text-[var(--text-muted)]">
            {soloEligibility.message || 'Ambiguous ownership or collaboration history detected. Controlled recovery is required before solo setup can be enabled.'}
          </p>
        </div>
      )}

      {setupStatus?.ownerCanManageOwnAccess && !isEligibleForSolo && (
        <div className="p-5 rounded-xl border border-sky-500/30 bg-sky-500/10 flex flex-col md:flex-row items-start md:items-center justify-between gap-4">
          <div>
            <h2 className="font-semibold text-sky-400">Set up your access</h2>
            <p className="text-sm text-[var(--text-muted)]">
              As the only active member, you can add functional roles for yourself. If another member joins, role changes require the normal approval process.
            </p>
          </div>
          <button
            type="button"
            className="px-4 py-2 rounded-lg bg-sky-600 hover:bg-sky-500 text-white font-medium whitespace-nowrap"
            onClick={() => openForm('setup')}
            disabled={busy}
          >
            Set up my access
          </button>
        </div>
      )}

      {form && (
        <form onSubmit={submit} className="p-5 rounded-xl bg-[var(--bg-surface)] space-y-4">
          <h2 className="font-semibold">
            {form === 'invite'
              ? 'Invite to this workspace'
              : form === 'request'
              ? 'Request functional roles'
              : form === 'setup'
              ? 'Set up your functional roles'
              : `Edit access for ${form.email}`}
          </h2>

          {form === 'invite' && (
            <>
              <label className="block">Email
                <input className={input} type="email" required value={email} onChange={e => setEmail(e.target.value)} autoComplete="email" />
              </label>
              <div className="rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-subtle)] p-3 text-sm space-y-1.5">
                <p className="font-medium text-[var(--text-main)]">Sharing this workspace</p>
                <p className="text-[var(--text-muted)]">
                  Your access stays as assigned
                  {currentOrg.operatingPolicy?.ownAccessProfileDisplayName
                    ? ` (${currentOrg.operatingPolicy.ownAccessProfileDisplayName})`
                    : ''}
                  . The invitee receives only the functional roles you select below.
                </p>
                <p className="text-[var(--text-muted)]">
                  {journalWorkflowLabel(currentOrg.operatingPolicy?.journalWorkflow)}
                  {currentOrg.operatingPolicy?.collaborationState === 'SoloActive'
                    ? ' Accepting this invitation activates team collaboration; it does not remove your access.'
                    : ''}
                </p>
              </div>
            </>
          )}

          {form === 'request' && canManage && (
            <label className="block">Target member
              <select
                className={input}
                value={requestTargetMemberId ?? ''}
                onChange={e => setRequestTargetMemberId(e.target.value ? Number(e.target.value) : null)}
              >
                <option value="">Myself ({user?.email})</option>
                {members.filter(m => m.userId !== user?.id && m.status === 'Active').map(m => (
                  <option key={m.memberId} value={m.memberId}>
                    {m.firstName} {m.lastName} ({m.email})
                  </option>
                ))}
              </select>
            </label>
          )}

          {form !== 'request' && form !== 'setup' && (
            typeof form === 'object' && form.role === 'Owner' ? (
              <p>Ownership is changed using Transfer ownership, not role editing.</p>
            ) : (
              <label className="block">Workspace role
                <select className={input} value={role} onChange={e => setRole(e.target.value as 'Admin' | 'Member')}>
                  <option value="Member">Member</option>
                  {isOwner && <option value="Admin">Admin</option>}
                </select>
              </label>
            )
          )}

          <fieldset className="space-y-2">
            <legend className="font-medium mb-2">Functional roles</legend>
            <p className="text-sm text-[var(--text-muted)]">
              Functional roles grant access to accounting, sales, purchasing, banking, and reporting operations. Workspace roles (Owner, Admin, Member) govern workspace administration only.
            </p>
            {roles.map(item => (
              <label key={item.id} className="flex items-center gap-2">
                <input
                  type="checkbox"
                  checked={roleIds.includes(item.id)}
                  onChange={e =>
                    setRoleIds(ids => e.target.checked ? [...ids, item.id] : ids.filter(id => id !== item.id))}
                />
                {item.displayName || item.name}
              </label>
            ))}
          </fieldset>

          {form === 'request' && (
            <label className="block">Reason / Business justification
              <input
                className={input}
                type="text"
                placeholder="e.g. Month-end closing responsibilities"
                value={requestReason}
                onChange={e => setRequestReason(e.target.value)}
              />
            </label>
          )}

          <div className="flex flex-wrap gap-2">
            <button type="submit" className={button} disabled={busy || ((form === 'request' || form === 'setup') && roleIds.length === 0)}>
              {busy
                ? 'Saving...'
                : form === 'invite'
                ? 'Send invitation'
                : form === 'request'
                ? 'Submit request'
                : form === 'setup'
                ? 'Save and activate roles'
                : 'Save access'}
            </button>
            {form !== 'invite' && form !== 'request' && form !== 'setup' && (
              <button
                type="button"
                className={button}
                disabled={busy || roleIds.length === 0}
                onClick={submitAsAccessRequest}
                title="Submit as an access request requiring independent approval if direct assignment is restricted"
              >
                Submit as access request
              </button>
            )}
            <button type="button" className={button} disabled={busy} onClick={() => setForm(null)}>Cancel</button>
          </div>
        </form>
      )}

      <label className="block max-w-md">Search members
        <input className={input} type="search" value={query} onChange={e => setQuery(e.target.value)} />
      </label>
      {loading ? <p role="status">Loading workspace members...</p> : (
        <div className="overflow-x-auto rounded-xl bg-[var(--bg-surface)]">
          <table className="w-full text-sm text-left">
            <caption className="sr-only">Members of {currentOrg.name}</caption>
            <thead><tr>{['Member', 'Workspace role', 'Functional roles', 'Status', 'Joined', 'Actions'].map(heading =>
              <th scope="col" className="p-4" key={heading}>{heading}</th>)}</tr></thead>
            <tbody>{visibleMembers.map(member => {
              const self = member.userId === user?.id;
              const editable = isOwner || (canManage && member.role === 'Member');
              return <tr key={member.memberId} className="border-t border-[var(--border-subtle)]">
                <td className="p-4"><div className="font-medium">{member.firstName} {member.lastName}{self ? ' (you)' : ''}</div><div>{member.email}</div></td>
                <td className="p-4">{member.role}</td>
                <td className="p-4">{member.securityRoles.map(r => r.displayName || r.name).join(', ') || 'None'}</td>
                <td className="p-4">{member.status}</td>
                <td className="p-4">{new Date(member.joinedAt).toLocaleDateString()}</td>
                <td className="p-4"><div className="flex flex-wrap gap-2">
                  {editable && <button type="button" className={button} disabled={busy} onClick={() => openForm(member)}>Edit access</button>}
                  {(editable || self) && <button type="button" className={button} disabled={busy} onClick={() => setPendingConfirm({
                    title: self ? 'Leave this workspace?' : 'Remove member?',
                    message: self
                      ? `You will lose access to ${currentOrg.name}. The last Owner must transfer ownership first.`
                      : `${member.email} will lose access to ${currentOrg.name}.`,
                    confirmLabel: self ? 'Leave workspace' : 'Remove member',
                    destructive: true,
                    action: () => mutate(`members/${member.memberId}`, 'DELETE', undefined, 'Member removed.', self),
                  })}>{self ? 'Leave' : 'Remove'}</button>}
                  {isOwner && !self && member.status === 'Active' && <button type="button" className={button} disabled={busy} onClick={() => setPendingConfirm({
                    title: 'Transfer ownership?',
                    message: `${member.email} will become the Owner of ${currentOrg.name}. You will become an Admin.`,
                    confirmLabel: 'Transfer ownership',
                    destructive: true,
                    action: () => mutate('transfer-ownership', 'POST', { memberId: member.memberId }, 'Ownership transferred.'),
                  })}>Transfer ownership</button>}
                </div></td>
              </tr>;
            })}</tbody>
          </table>
          {visibleMembers.length === 0 && <p className="p-4">No matching members.</p>}
        </div>
      )}

      {/* Access Requests & Approvals Section */}
      <section className="p-5 rounded-xl bg-[var(--bg-surface)] space-y-3">
        <div className="flex justify-between items-center">
          <h2 className="text-lg font-semibold">Access requests & approvals</h2>
          <span className="text-sm text-[var(--text-muted)]">
            {accessRequests.filter(r => r.status === 'Pending').length} pending
          </span>
        </div>
        {accessRequests.length === 0 && <p className="text-[var(--text-muted)]">No access requests.</p>}
        {accessRequests.map(req => {
          const isPending = req.status === 'Pending';
          return (
            <div key={req.requestId} className="flex flex-wrap justify-between items-center gap-3 border-t border-[var(--border-subtle)] pt-3">
              <div className="space-y-1">
                <div className="flex items-center gap-2">
                  <span className="font-medium">{req.targetEmail}</span>
                  <span className={`text-xs px-2 py-0.5 rounded-full ${
                    req.status === 'Applied' || req.status === 'Approved'
                      ? 'bg-emerald-500/20 text-emerald-400'
                      : req.status === 'Pending'
                      ? 'bg-amber-500/20 text-amber-400'
                      : req.status === 'Rejected'
                      ? 'bg-red-500/20 text-red-400'
                      : 'bg-zinc-500/20 text-zinc-400'
                  }`}>
                    {req.status}
                  </span>
                </div>
                <p className="text-sm">
                  Roles requested: <span className="text-[var(--text-main)] font-medium">{req.requestedRoleNames?.join(', ') || 'None'}</span>
                </p>
                <p className="text-xs text-[var(--text-muted)]">
                  Requested by {req.requesterEmail} · {new Date(req.createdAtUtc).toLocaleDateString()}
                  {req.reason ? ` · Reason: "${req.reason}"` : ''}
                  {req.approverEmail ? ` · Reviewed by: ${req.approverEmail}` : ''}
                </p>
              </div>
              {isPending && (
                <div className="flex gap-2">
                  {req.canApprove && (
                    <>
                      <button
                        type="button"
                        className="px-3 py-1.5 rounded-lg bg-emerald-600 hover:bg-emerald-500 text-white text-sm font-medium"
                        disabled={busy}
                        onClick={() => setPendingConfirm({
                          title: 'Approve access request?',
                          message: `Approve functional roles (${req.requestedRoleNames?.join(', ')}) for ${req.targetEmail}? Roles will be immediately applied.`,
                          confirmLabel: 'Approve request',
                          destructive: false,
                          action: () => mutate(`access-requests/${req.requestId}/approve`, 'POST', {}, 'Access request approved and applied.')
                        })}
                      >
                        Approve
                      </button>
                      <button
                        type="button"
                        className="px-3 py-1.5 rounded-lg border border-red-500/30 text-red-400 hover:bg-red-500/10 text-sm"
                        disabled={busy}
                        onClick={() => setPendingConfirm({
                          title: 'Reject access request?',
                          message: `Reject access request for ${req.targetEmail}?`,
                          confirmLabel: 'Reject request',
                          destructive: true,
                          action: () => mutate(`access-requests/${req.requestId}/reject`, 'POST', {}, 'Access request rejected.')
                        })}
                      >
                        Reject
                      </button>
                    </>
                  )}
                  {req.canCancel && (
                    <button
                      type="button"
                      className="px-3 py-1.5 rounded-lg border border-[var(--border-subtle)] hover:bg-[var(--bg-subtle)] text-sm"
                      disabled={busy}
                      onClick={() => setPendingConfirm({
                        title: 'Cancel access request?',
                        message: `Cancel this pending access request for ${req.targetEmail}?`,
                        confirmLabel: 'Cancel request',
                        destructive: true,
                        action: () => mutate(`access-requests/${req.requestId}/cancel`, 'POST', undefined, 'Access request cancelled.')
                      })}
                    >
                      Cancel
                    </button>
                  )}
                </div>
              )}
            </div>
          );
        })}
      </section>

      {canManage && <section className="p-5 rounded-xl bg-[var(--bg-surface)] space-y-3">
        <h2 className="text-lg font-semibold">Pending invitations</h2>
        {invitations.length === 0 && <p className="text-[var(--text-muted)]">No pending invitations.</p>}
        {invitations.map(invite => <div key={invite.id} className="flex flex-wrap justify-between items-center gap-3 border-t border-[var(--border-subtle)] pt-3">
          <div><p>{invite.email} · {invite.role}</p>
            <p className="text-sm text-[var(--text-muted)]">Expires {new Date(invite.expiresAt).toLocaleString()}</p></div>
          {(isOwner || invite.role === 'Member') && <div className="flex gap-2">
            <button type="button" className={button} disabled={busy} onClick={() =>
              void mutate(`invitations/${invite.id}/resend`, 'POST', undefined, 'Invitation email resent.')
            }>Resend</button>
            <button type="button" className={button} disabled={busy} onClick={() => setPendingConfirm({
              title: 'Revoke invitation?',
              message: `The invitation link sent to ${invite.email} will stop working.`,
              confirmLabel: 'Revoke invitation',
              destructive: true,
              action: () => mutate(`invitations/${invite.id}`, 'DELETE', undefined, 'Invitation revoked.'),
            })}>Revoke</button>
          </div>}
        </div>)}
      </section>}

      {pendingConfirm && (
        <ConfirmDialog
          title={pendingConfirm.title}
          message={pendingConfirm.message}
          confirmLabel={pendingConfirm.confirmLabel}
          destructive={pendingConfirm.destructive}
          busy={busy}
          onCancel={() => setPendingConfirm(null)}
          onConfirm={() => {
            const { action } = pendingConfirm;
            void action().finally(() => setPendingConfirm(null));
          }}
        />
      )}
    </div>
  );
};

export default Members;
