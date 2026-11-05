/** Pure helpers for the workspace announcement strip (testable without React). */

export type AnnouncementSeverity = 'info' | 'warning' | 'critical';

export interface AnnouncementItem {
  id: string;
  severity: AnnouncementSeverity;
  message: string;
  action?: { label: string; href: string } | null;
  dismissible: boolean;
  audience?: string;
  expiresAt?: string | null;
  /** Client-only items (e.g. past-due) are not dismissed via the API list. */
  clientOnly?: boolean;
}

const SEVERITY_RANK: Record<AnnouncementSeverity, number> = {
  critical: 0,
  warning: 1,
  info: 2,
};

export function sortAnnouncements(items: AnnouncementItem[]): AnnouncementItem[] {
  return [...items].sort((a, b) => {
    const rank = (SEVERITY_RANK[a.severity] ?? 99) - (SEVERITY_RANK[b.severity] ?? 99);
    return rank !== 0 ? rank : a.id.localeCompare(b.id);
  });
}

export function filterVisibleAnnouncements(
  items: AnnouncementItem[],
  dismissedIds: ReadonlySet<string>,
  nowMs: number = Date.now(),
): AnnouncementItem[] {
  return sortAnnouncements(
    items.filter((item) => {
      if (dismissedIds.has(item.id)) return false;
      if (item.expiresAt) {
        const expires = Date.parse(item.expiresAt);
        if (!Number.isNaN(expires) && expires <= nowMs) return false;
      }
      return true;
    }),
  );
}

/** Mobile (<640px): first item plus a "+N more" count for the rest. */
export function mobileCollapse(items: AnnouncementItem[]): {
  first: AnnouncementItem | null;
  moreCount: number;
} {
  if (items.length === 0) return { first: null, moreCount: 0 };
  return { first: items[0], moreCount: Math.max(0, items.length - 1) };
}

export function dismissalStorageKey(userId: string, id: string): string {
  return `ag:announcement-dismissed:${userId}:${id}`;
}

export function readDismissedIds(storage: Storage | null | undefined, userId: string, ids: string[]): Set<string> {
  const dismissed = new Set<string>();
  if (!storage || !userId) return dismissed;
  for (const id of ids) {
    try {
      if (storage.getItem(dismissalStorageKey(userId, id)) === '1') dismissed.add(id);
    } catch {
      /* storage may throw (private mode / quota) */
    }
  }
  return dismissed;
}

export function writeDismissal(storage: Storage | null | undefined, userId: string, id: string): void {
  if (!storage || !userId) return;
  try {
    storage.setItem(dismissalStorageKey(userId, id), '1');
  } catch {
    /* ignore */
  }
}

/** True when the server already sent a billing notice (id prefix `billing:`). */
export function hasServerBillingAnnouncement(items: readonly AnnouncementItem[]): boolean {
  return items.some((item) => typeof item.id === 'string' && item.id.startsWith('billing:'));
}

/**
 * Merge remote announcements with an optional client past-due item.
 * When any remote id starts with `billing:`, skip the client past-due item so the
 * server notice (with the exact date) is the only one shown.
 */
export function mergeAnnouncementItems(
  remote: readonly AnnouncementItem[],
  clientPastDue: AnnouncementItem | null,
): AnnouncementItem[] {
  const items = [...remote];
  if (clientPastDue && !hasServerBillingAnnouncement(remote)) {
    items.push(clientPastDue);
  }
  return items;
}
