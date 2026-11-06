import type { Entitlements } from './entitlements';
import type { EntitlementProblem } from './entitlementEvents';

/** Permission that gates every activity / history / security-log request. */
export const AUDIT_LOGS_VIEW_PERMISSION = 'audit.logs.view';
/** Feature keys; the server stays the authority and answers FEATURE_NOT_ENTITLED with these. */
export const FEATURE_AUDIT_ACTIVITY = 'audit.activity';
export const FEATURE_AUDIT_HISTORY_FULL = 'audit.history.full';

/** Days of history the activity list covers when only the activity feature is included. */
export const AUDIT_RECENT_WINDOW_DAYS = 90;

export type AuditAccessLevel = 'none' | 'singleRecord' | 'recent90' | 'full';

type PermissionCheck = (permission: string | string[]) => boolean;

/**
 * What the viewer can open, mirroring `AuditHistoryAccess.Evaluate`:
 * no permission → none; community edition or full-history feature → full;
 * activity feature → recent90; otherwise a single record's History only.
 * No snapshot yet fails closed to single-record History (always available to permitted users).
 */
export function auditAccessLevel(
  snapshot: Pick<Entitlements, 'plan' | 'effectivePlan' | 'features'> | null,
  hasPermission: PermissionCheck,
): AuditAccessLevel {
  if (!hasPermission(AUDIT_LOGS_VIEW_PERMISSION)) return 'none';
  if (!snapshot) return 'singleRecord';
  const features = snapshot.features ?? [];
  // Community edition rule, same as evaluateCanStatementImport.
  const plan = (snapshot.effectivePlan || snapshot.plan || '').toLowerCase();
  if (plan === 'community' || features.includes(FEATURE_AUDIT_HISTORY_FULL)) return 'full';
  if (features.includes(FEATURE_AUDIT_ACTIVITY)) return 'recent90';
  return 'singleRecord';
}

/** The History button on a single record shows for every level except none. */
export function canShowRecordHistory(access: AuditAccessLevel): boolean {
  return access !== 'none';
}

export type ActivityTabMode = 'hidden' | 'planLimit' | 'list';

/**
 * Settings → Activity tab:
 * recent90 / full → the list; singleRecord → a plan-limit card in a business workspace,
 * hidden in personal books (no upgrade prompt there); none → hidden.
 */
export function activityTabMode(access: AuditAccessLevel, personal: boolean): ActivityTabMode {
  if (access === 'recent90' || access === 'full') return 'list';
  if (access === 'singleRecord' && !personal) return 'planLimit';
  return 'hidden';
}

export interface Recent90Notice {
  /** Text before the optional upgrade link. */
  lead: string;
  /** Link label, present only when the viewer can manage billing. */
  linkLabel: string | null;
  /** Text after the link. */
  tail: string;
}

const RECENT_90_LEAD = `Showing the last ${AUDIT_RECENT_WINDOW_DAYS} days. Your plan keeps the full history`;

/** The banner above a 90-day list: never in personal books, no "upgrade" without billing permission. */
export function recent90Notice(
  access: AuditAccessLevel,
  personal: boolean,
  canManageBilling: boolean,
): Recent90Notice | null {
  if (access !== 'recent90' || personal) return null;
  if (!canManageBilling) return { lead: `${RECENT_90_LEAD}.`, linkLabel: null, tail: '' };
  return { lead: `${RECENT_90_LEAD}; `, linkLabel: 'upgrade', tail: ' to see all of it.' };
}

export function recent90NoticeText(notice: Recent90Notice): string {
  return `${notice.lead}${notice.linkLabel ?? ''}${notice.tail}`;
}

/** Problem document for the inline plan-limit card (same shape the server's refusal uses). */
export function activityPlanLimitProblem(canManageBilling: boolean): EntitlementProblem {
  return {
    code: 'FEATURE_NOT_ENTITLED',
    requiredFeature: FEATURE_AUDIT_ACTIVITY,
    detail: 'The activity list is not included in your plan. You can still open History on any single record.',
    canManageBilling,
    upgradeAvailable: false,
  };
}

/** Security log and CSV export need the full level. */
export function canViewSecurityLog(access: AuditAccessLevel): boolean {
  return access === 'full';
}

export function canExportActivity(access: AuditAccessLevel): boolean {
  return access === 'full';
}
