import { auditEntityName, isAuditLineEntity, type AuditChangeRow } from './auditLabels';

/** One row of GET /api/organizations/current/activity. */
export interface ActivityItem extends AuditChangeRow {
  /** The audit row's own sequence number (not a record id), so it stays numeric. */
  id: number;
  /** The record's public id, or null for a record type that has none. */
  entityId: string | null;
  parentEntityType?: string | null;
  parentEntityId?: string | null;
  actorUserId?: string | null;
  actorName?: string | null;
  actorKind?: string | null;
  correlationId?: string | null;
  occurredAt: string;
}

export interface ActivityPage {
  items: ActivityItem[];
  nextCursor: number | null;
  windowDays?: number | null;
}

/** One row of GET /api/organizations/current/security-events. */
export interface SecurityEventItem {
  id: number;
  type: string;
  outcome?: string | null;
  actorUserId?: string | null;
  actorName?: string | null;
  ipAddress?: string | null;
  createdAt: string;
}

export interface SecurityEventPage {
  items: SecurityEventItem[];
  nextCursor: number | null;
}

export const ACTIVITY_PAGE_SIZE = 50;
export const ACTIVITY_ENDPOINT = '/api/organizations/current/activity';
export const ACTIVITY_EXPORT_ENDPOINT = '/api/organizations/current/activity/export';
export const SECURITY_EVENTS_ENDPOINT = '/api/organizations/current/security-events';

type QueryValue = string | number | boolean | null | undefined;

/** Drops empty values; keeps a stable key order so URLs are predictable. */
export function buildQueryString(params: Record<string, QueryValue>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value === null || value === undefined || value === '') continue;
    search.set(key, String(value));
  }
  const text = search.toString();
  return text ? `?${text}` : '';
}

export function recordHistoryUrl(entityType: string, entityId: string, cursor?: number | null): string {
  return `${ACTIVITY_ENDPOINT}${buildQueryString({
    entityType,
    entityId,
    includeChildren: true,
    limit: ACTIVITY_PAGE_SIZE,
    cursor: cursor ?? undefined,
  })}`;
}

export interface ActivityFilters {
  actorUserId?: string;
  entityType?: string;
  /** Local calendar dates, YYYY-MM-DD. */
  fromDate?: string;
  toDate?: string;
}

/** Start of the local day / end of the local day as ISO instants (the server compares in UTC). */
export function dateRangeToInstants(filters: Pick<ActivityFilters, 'fromDate' | 'toDate'>): { from?: string; to?: string } {
  const from = filters.fromDate ? new Date(`${filters.fromDate}T00:00:00`) : null;
  const to = filters.toDate ? new Date(`${filters.toDate}T23:59:59.999`) : null;
  return {
    from: from && !Number.isNaN(from.getTime()) ? from.toISOString() : undefined,
    to: to && !Number.isNaN(to.getTime()) ? to.toISOString() : undefined,
  };
}

export function activityListUrl(filters: ActivityFilters, cursor?: number | null, forExport = false): string {
  const { from, to } = dateRangeToInstants(filters);
  return `${forExport ? ACTIVITY_EXPORT_ENDPOINT : ACTIVITY_ENDPOINT}${buildQueryString({
    entityType: filters.entityType,
    actorUserId: filters.actorUserId,
    from,
    to,
    limit: forExport ? undefined : ACTIVITY_PAGE_SIZE,
    cursor: forExport ? undefined : cursor ?? undefined,
  })}`;
}

export interface SecurityEventFilters {
  type?: string;
  actorUserId?: string;
  fromDate?: string;
  toDate?: string;
}

export function securityEventsUrl(filters: SecurityEventFilters, cursor?: number | null): string {
  const { from, to } = dateRangeToInstants(filters);
  return `${SECURITY_EVENTS_ENDPOINT}${buildQueryString({
    type: filters.type,
    actorUserId: filters.actorUserId,
    from,
    to,
    limit: ACTIVITY_PAGE_SIZE,
    cursor: cursor ?? undefined,
  })}`;
}

/** Server problem documents carry `detail`; plain errors carry `message` or `title`. */
export function problemMessage(body: unknown, fallback: string): string {
  if (body && typeof body === 'object') {
    const record = body as Record<string, unknown>;
    for (const key of ['detail', 'message', 'title']) {
      const value = record[key];
      if (typeof value === 'string' && value.trim()) return value;
    }
  }
  return fallback;
}

export function isLineRow(item: Pick<ActivityItem, 'entityType' | 'parentEntityType'>): boolean {
  return !!item.parentEntityType || isAuditLineEntity(item.entityType);
}

export interface HistoryPart {
  /** null for the record itself; "Line 3" for a line. */
  heading: string | null;
  rows: ActivityItem[];
}

export interface HistoryGroup {
  key: string;
  occurredAt: string;
  actorKind?: string | null;
  actorName?: string | null;
  parts: HistoryPart[];
}

/**
 * One group per save (same correlation id), newest first. The record's own changes come first;
 * each line's changes follow under "Line {entityId}".
 */
export function groupHistoryBySave(items: readonly ActivityItem[]): HistoryGroup[] {
  const groups = new Map<string, { group: HistoryGroup; lineParts: Map<string, HistoryPart>; recordPart: HistoryPart }>();
  for (const item of items) {
    const groupKey = item.correlationId ? `c:${item.correlationId}` : `i:${item.id}`;
    let entry = groups.get(groupKey);
    if (!entry) {
      const recordPart: HistoryPart = { heading: null, rows: [] };
      entry = {
        group: {
          key: groupKey,
          occurredAt: item.occurredAt,
          actorKind: item.actorKind,
          actorName: item.actorName,
          parts: [recordPart],
        },
        lineParts: new Map(),
        recordPart,
      };
      groups.set(groupKey, entry);
    }
    if (!isLineRow(item)) {
      entry.recordPart.rows.push(item);
      continue;
    }
    const heading = `Line ${item.entityId}`;
    const lineKey = `${item.entityType}:${item.entityId}`;
    let part = entry.lineParts.get(lineKey);
    if (!part) {
      part = { heading, rows: [] };
      entry.lineParts.set(lineKey, part);
      entry.group.parts.push(part);
    }
    part.rows.push(item);
  }
  return Array.from(groups.values()).map(({ group }) => ({
    ...group,
    parts: [group.parts[0], ...group.parts.slice(1).sort((a, b) => (a.rows[0].entityId ?? '').localeCompare(b.rows[0].entityId ?? ''))]
      .filter((part) => part.rows.length > 0),
  }));
}

/** "Invoice inv_…", or "Invoice inv_… · Line il_…" for a line whose document is known; the id is left out for a record type that has none. */
export function activityRecordLabel(item: ActivityItem, personal: boolean): string {
  const line = item.entityId ? `Line ${item.entityId}` : 'Line';
  if (item.parentEntityType && item.parentEntityId) {
    return `${auditEntityName(item.parentEntityType, personal)} #${item.parentEntityId} · ${line}`;
  }
  return item.entityId
    ? `${auditEntityName(item.entityType, personal)} #${item.entityId}`
    : auditEntityName(item.entityType, personal);
}

export interface PersonOption {
  value: string;
  label: string;
}

/** Workspace members as filter choices: full name, or email when no name is set. */
export function personFilterOptions(
  members: ReadonlyArray<{ userId: string; email?: string; firstName?: string; lastName?: string }>,
): PersonOption[] {
  return members.map((member) => {
    const name = `${member.firstName ?? ''} ${member.lastName ?? ''}`.trim();
    return { value: member.userId, label: name || member.email || member.userId };
  });
}
