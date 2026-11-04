import React, { useCallback, useEffect, useState } from 'react';
import { AlertCircle, CheckCircle2, ShieldCheck } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';
import type { JournalApprovalSummary, WorkspaceOperatingPolicySummary } from '../types';

const OPERATING_SETTINGS_PATH = '/api/organizations/current/operating-settings';
const POLICY_CHANGE_PATH = `${OPERATING_SETTINGS_PATH}/journal-approval-policy`;

const INDEPENDENT_MODE = 'IndependentReviewerRequired';
const SINGLE_ACTOR_MODE = 'SingleActorAllowed';

interface ProblemBody {
  detail?: string;
  title?: string;
  message?: string;
  reasonCode?: string;
}

export interface JournalApprovalPolicyError {
  message: string;
  reasonCode?: string;
}

export function describeJournalApprovalMode(mode?: string | null): string {
  switch (mode) {
    case INDEPENDENT_MODE:
      return 'Independent review required: a different Owner or Admin who can post journals must approve each journal before it is posted.';
    case SINGLE_ACTOR_MODE:
      return 'Single-user posting: the person who prepares a journal may also post it.';
    default:
      return 'Journal posting follows the workspace default, which keeps preparer and approver separate.';
  }
}

export async function readJournalApprovalProblem(response: Response): Promise<JournalApprovalPolicyError> {
  const problem: ProblemBody | null = await response.json().catch(() => null);
  const message = problem?.detail || problem?.message || problem?.title || `Request failed (${response.status})`;
  return { message, reasonCode: problem?.reasonCode };
}

/**
 * Effective journal approval policy with an explicit, revision-checked Owner change.
 * Nothing here changes the policy implicitly: inviting members, changing roles, or member count never relax it.
 */
export const JournalApprovalPolicyCard: React.FC<{ isOwner: boolean }> = ({ isOwner }) => {
  const { authFetch } = useAuth();
  const [settings, setSettings] = useState<WorkspaceOperatingPolicySummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<JournalApprovalPolicyError | null>(null);
  const [notice, setNotice] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const response = await authFetch(OPERATING_SETTINGS_PATH);
      if (!response.ok) {
        setError(await readJournalApprovalProblem(response));
        return;
      }
      const parsed = await response.json();
      setSettings(parsed && !Array.isArray(parsed) ? parsed : null);
    } catch (err) {
      setError({ message: err instanceof Error ? err.message : 'Could not load the journal approval policy.' });
    } finally {
      setLoading(false);
    }
  }, [authFetch]);

  useEffect(() => {
    void load();
  }, [load]);

  const approval: JournalApprovalSummary | null | undefined = settings?.journalApproval;
  if (loading) return null;
  if (!approval) {
    return error ? (
      <div role="alert" className="p-4 rounded-2xl bg-red-500/10 text-red-400 text-xs font-semibold flex items-center gap-2.5">
        <AlertCircle className="w-4 h-4 shrink-0" />
        <span>{error.message}</span>
      </div>
    ) : null;
  }

  const isIndependent = approval.mode === INDEPENDENT_MODE;
  const targetMode = isIndependent ? SINGLE_ACTOR_MODE : INDEPENDENT_MODE;
  const canChange = isOwner && approval.canChange;

  const change = async () => {
    if (settings?.policyRevision === undefined) return;
    setBusy(true);
    setError(null);
    setNotice('');
    try {
      const response = await authFetch(POLICY_CHANGE_PATH, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          expectedPolicyRevision: settings.policyRevision,
          journalWorkflow: targetMode,
        }),
      });
      if (!response.ok) {
        const problem = await readJournalApprovalProblem(response);
        setError(problem);
        // A stale revision means another change landed first; reload so the next attempt uses the latest.
        if (response.status === 409) await load();
        return;
      }
      setNotice(
        targetMode === INDEPENDENT_MODE
          ? 'Independent review is now required for journals.'
          : 'Journals can now be posted by the person who prepared them.',
      );
      await load();
    } catch (err) {
      setError({ message: err instanceof Error ? err.message : 'Could not change the journal approval policy.' });
    } finally {
      setBusy(false);
    }
  };

  return (
    <section aria-labelledby="journal-approval-heading" className="rounded-2xl bg-[var(--bg-card)] shadow-sm p-5 space-y-4">
      <div className="flex items-center gap-2">
        <ShieldCheck className="w-4 h-4 text-[var(--brand-primary)]" />
        <h2 id="journal-approval-heading" className="text-sm font-semibold text-[var(--text-main)]">
          Journal approval policy
        </h2>
      </div>

      <p className="text-sm text-[var(--text-secondary)]" data-testid="journal-approval-mode">
        {describeJournalApprovalMode(approval.mode)}
      </p>
      <dl className="grid grid-cols-1 sm:grid-cols-2 gap-3 text-xs text-[var(--text-secondary)]">
        <div>
          <dt className="font-semibold">Set by</dt>
          <dd>{approval.provenance === 'ExplicitPolicyChange' ? 'An explicit Owner change' : 'Workspace default'}</dd>
        </div>
        <div>
          <dt className="font-semibold">Other reviewers who can approve</dt>
          <dd data-testid="journal-approval-reviewers">{approval.eligibleOtherReviewerCount}</dd>
        </div>
      </dl>

      {notice && (
        <div role="status" className="p-3 rounded-xl bg-emerald-500/10 text-emerald-400 text-xs font-semibold flex items-center gap-2">
          <CheckCircle2 className="w-4 h-4 shrink-0" />
          <span>{notice}</span>
        </div>
      )}
      {error && (
        <div role="alert" className="p-3 rounded-xl bg-red-500/10 text-red-400 text-xs font-semibold flex items-center gap-2" data-reason-code={error.reasonCode}>
          <AlertCircle className="w-4 h-4 shrink-0" />
          <span>{error.message}</span>
        </div>
      )}

      {canChange ? (
        <div className="space-y-2">
          <p className="text-xs text-[var(--text-secondary)]">
            {isIndependent
              ? 'Switching to single-user posting removes the second-person check for journals. It is recorded in the audit trail with your name.'
              : 'Requiring independent review needs at least one other Owner or Admin who can post journals.'}
          </p>
          <button
            type="button"
            onClick={() => void change()}
            disabled={busy}
            className="px-4 py-2 rounded-xl bg-[var(--brand-primary)] text-white text-xs font-semibold disabled:opacity-50"
          >
            {isIndependent ? 'Allow single-user posting' : 'Require independent review'}
          </button>
        </div>
      ) : (
        <p className="text-xs text-[var(--text-secondary)]">Only a workspace Owner can change this policy.</p>
      )}
    </section>
  );
};

export default JournalApprovalPolicyCard;
