import type { WorkspaceOperatingPolicySummary } from '../types';
import { describeJournalApprovalMode } from '../components/JournalApprovalPolicyCard';

const PURPOSE_LABEL: Record<string, string> = {
  Personal: 'Personal finances',
  Business: 'Business or freelance work',
};

const COLLABORATION_LABEL: Record<string, string> = {
  JustMe: 'Just me',
  WithOthers: 'Me and others',
};

const COLLABORATION_STATE_LABEL: Record<string, string> = {
  SoloActive: 'Solo',
  TeamSetupPending: 'Team setup in progress',
  TeamActive: 'Team active',
  LegacyUnclassified: 'Not classified yet',
};

function journalPostingLabel(settings: WorkspaceOperatingPolicySummary): string {
  // Prefer the same wording as CreateOrg Review when intent is known.
  if (settings.collaborationIntent === 'JustMe') {
    return 'You can prepare and post your own journals under the single-user workflow.';
  }
  if (settings.collaborationIntent === 'WithOthers') {
    return 'Independent review: another Owner or Admin who can post journals approves each journal before it is posted.';
  }
  const mode = settings.journalWorkflow || settings.journalApproval?.mode;
  return mode ? describeJournalApprovalMode(mode) : 'Not set';
}

/** Rows matching CreateOrg Review (about answers) for display in Organization Settings. */
export function buildWorkspaceSetupRows(
  settings: WorkspaceOperatingPolicySummary,
): [string, string][] {
  const purpose = settings.purpose && PURPOSE_LABEL[settings.purpose]
    ? PURPOSE_LABEL[settings.purpose]
    : settings.purpose && settings.purpose !== 'Unspecified'
      ? settings.purpose
      : 'Not set';
  const who = settings.collaborationIntent && COLLABORATION_LABEL[settings.collaborationIntent]
    ? COLLABORATION_LABEL[settings.collaborationIntent]
    : settings.collaborationIntent && settings.collaborationIntent !== 'Unspecified'
      ? settings.collaborationIntent
      : 'Not set';

  const justMe = settings.collaborationIntent === 'JustMe';
  const withOthers = settings.collaborationIntent === 'WithOthers';
  const access = settings.ownAccessProfileDisplayName
    ? `Owner · ${settings.ownAccessProfileDisplayName}`
    : justMe || withOthers
      ? 'Owner · Full workspace access'
      : 'Not set';

  const rows: [string, string][] = [
    ['Purpose', purpose],
    ['Who uses it', who],
    ['Your access', access],
    ['Journal posting', journalPostingLabel(settings)],
  ];

  if (settings.collaborationState && settings.collaborationState !== 'LegacyUnclassified') {
    rows.push([
      'Collaboration status',
      COLLABORATION_STATE_LABEL[settings.collaborationState] ?? settings.collaborationState,
    ]);
  }

  return rows;
}

export function workspaceSetupIsConfigured(settings: WorkspaceOperatingPolicySummary | null | undefined): boolean {
  if (!settings) return false;
  return Boolean(
    (settings.purpose && settings.purpose !== 'Unspecified')
    || (settings.collaborationIntent && settings.collaborationIntent !== 'Unspecified'),
  );
}
