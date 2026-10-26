import React, { useCallback, useEffect, useState } from 'react';
import { useNavigate, useOutletContext } from 'react-router-dom';
import { RefreshCw, UserPlus } from 'lucide-react';
import { RoleItem, TenantContextType, WorkspaceInvitation, WorkspaceMember, WorkspaceRole } from '../types';
import { useAuth } from '../auth/AuthContext';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { PageHeader, HeaderButton } from '../components/PageHeader';

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
  throw new Error(message);
}

export const Members: React.FC = () => {
  const { currentOrg } = useOutletContext<TenantContextType>();
  const { authFetch, user } = useAuth();
  const navigate = useNavigate();
  const [members, setMembers] = useState<WorkspaceMember[]>([]);
  const [invitations, setInvitations] = useState<WorkspaceInvitation[]>([]);
  const [roles, setRoles] = useState<RoleItem[]>([]);
  const [currentRole, setCurrentRole] = useState<WorkspaceRole>('Member');
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  const [query, setQuery] = useState('');
  const [form, setForm] = useState<'invite' | WorkspaceMember | null>(null);
  const [email, setEmail] = useState('');
  const [role, setRole] = useState<'Admin' | 'Member'>('Member');
  const [roleIds, setRoleIds] = useState<number[]>([]);
  const [pendingConfirm, setPendingConfirm] = useState<PendingConfirm | null>(null);
  const canManage = currentRole === 'Owner' || currentRole === 'Admin';
  const isOwner = currentRole === 'Owner';

  const load = useCallback(async (signal?: AbortSignal) => {
    setLoading(true);
    setError('');
    setMembers([]);
    setInvitations([]);
    setRoles([]);
    setCurrentRole('Member');
    try {
      const current = await authFetch(base, { signal });
      await requireSuccess(current);
      const workspace: { role: WorkspaceRole } = await current.json();
      setCurrentRole(workspace.role);
      const responses = await Promise.all([
        authFetch(`${base}/members`, { signal }),
        authFetch(`${base}/security-roles`, { signal }),
        ...(workspace.role !== 'Member' ? [authFetch(`${base}/invitations`, { signal })] : []),
      ]);
      await Promise.all(responses.map(requireSuccess));
      const [memberRows, roleRows, inviteRows] = await Promise.all(responses.map(res => res.json()));
      if (signal?.aborted) return;
      setMembers(memberRows);
      setRoles(roleRows);
      setInvitations(inviteRows || []);
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
      setForm(null);
      setNotice(success);
      if (leaving) navigate('/select-org', { replace: true });
      else await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Workspace change failed.');
    } finally {
      setBusy(false);
    }
  };

  const openForm = (target: 'invite' | WorkspaceMember) => {
    setForm(target);
    setEmail(target === 'invite' ? '' : target.email);
    setRole(target === 'invite' || target.role === 'Owner' ? 'Member' : target.role);
    setRoleIds(target === 'invite' ? [] : target.securityRoles.map(r => r.id));
    setError('');
  };

  const submit = (event: React.FormEvent) => {
    event.preventDefault();
    if (!form) return;
    if (form === 'invite') {
      void mutate('invitations', 'POST', { email: email.trim(), role, securityRoleIds: roleIds }, 'Invitation email sent.');
    } else {
      void mutate(`members/${form.memberId}`, 'PATCH', {
        ...(form.role !== 'Owner' ? { role } : {}),
        securityRoleIds: roleIds,
      }, 'Workspace membership updated.');
    }
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
            {canManage && (
              <HeaderButton label="Invite member" icon={UserPlus} variant="primary" disabled={busy || loading} onClick={() => openForm('invite')} />
            )}
          </>
        }
      />
      {error && <div role="alert" className="p-4 rounded-xl bg-red-500/10 text-red-400">{error}</div>}
      {notice && <div role="status" className="p-4 rounded-xl bg-emerald-500/10 text-emerald-400">{notice}</div>}

      {form && canManage && (
        <form onSubmit={submit} className="p-5 rounded-xl bg-[var(--bg-surface)] space-y-4">
          <h2 className="font-semibold">{form === 'invite' ? 'Invite to this workspace' : `Edit access for ${form.email}`}</h2>
          {form === 'invite' && <label className="block">Email
            <input className={input} type="email" required value={email} onChange={e => setEmail(e.target.value)} autoComplete="email" />
          </label>}
          {form !== 'invite' && form.role === 'Owner' ? (
            <p>Ownership is changed using Transfer ownership, not role editing.</p>
          ) : <label className="block">Workspace role
            <select className={input} value={role} onChange={e => setRole(e.target.value as 'Admin' | 'Member')}>
              <option value="Member">Member</option>
              {isOwner && <option value="Admin">Admin</option>}
            </select>
          </label>}
          <fieldset className="space-y-2">
            <legend className="font-medium mb-2">Security roles</legend>
            <p className="text-sm text-[var(--text-muted)]">Members receive these permissions. Owners and Admins have full workspace permissions.</p>
            {roles.map(item => <label key={item.id} className="flex items-center gap-2">
              <input type="checkbox" checked={roleIds.includes(item.id)} onChange={e =>
                setRoleIds(ids => e.target.checked ? [...ids, item.id] : ids.filter(id => id !== item.id))} />
              {item.displayName || item.name}
            </label>)}
          </fieldset>
          <div className="flex gap-2">
            <button type="submit" className={button} disabled={busy}>{busy ? 'Saving...' : form === 'invite' ? 'Send invitation' : 'Save access'}</button>
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
            <thead><tr>{['Member', 'Role', 'Security roles', 'Status', 'Joined', 'Actions'].map(heading =>
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
