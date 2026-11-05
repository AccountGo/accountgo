import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { AlertCircle, AlertTriangle, Info, X } from 'lucide-react';
import { useAuth } from '../auth/AuthContext';
import { useBilling } from '../lib/useBilling';
import {
  type AnnouncementItem,
  filterVisibleAnnouncements,
  mergeAnnouncementItems,
  mobileCollapse,
  readDismissedIds,
  writeDismissal,
} from '../lib/announcementBarLogic';

const SEVERITY_STYLES: Record<AnnouncementItem['severity'], string> = {
  critical: 'border-rose-500/30 bg-rose-500/10 text-rose-700 dark:text-rose-300',
  warning: 'border-amber-500/30 bg-amber-500/10 text-amber-700 dark:text-amber-300',
  info: 'border-sky-500/30 bg-sky-500/10 text-sky-800 dark:text-sky-200',
};

const SEVERITY_ICON = {
  critical: AlertCircle,
  warning: AlertTriangle,
  info: Info,
} as const;

function usePastDueAnnouncement(): AnnouncementItem | null {
  const { billingEnabled, isPastDue, portalAvailable } = useBilling();
  if (!billingEnabled || !isPastDue) return null;
  return {
    id: 'client:past-due',
    severity: 'warning',
    message:
      'Payment past due. Update your payment method to keep paid features. Access continues during a short grace period.',
    action: portalAvailable ? { label: 'Manage billing', href: '__portal__' } : null,
    dismissible: false,
    clientOnly: true,
  };
}

const AnnouncementRow: React.FC<{
  item: AnnouncementItem;
  onDismiss?: (id: string) => void;
  onAction: (item: AnnouncementItem) => void;
}> = ({ item, onDismiss, onAction }) => {
  const Icon = SEVERITY_ICON[item.severity];
  return (
    <div
      role="status"
      className={`flex flex-col sm:flex-row sm:items-center justify-between gap-2 sm:gap-3 border-b px-3 sm:px-4 py-2.5 text-sm last:border-b-0 ${SEVERITY_STYLES[item.severity]}`}
    >
      <div className="flex items-start gap-2 min-w-0">
        <Icon className="w-4 h-4 shrink-0 mt-0.5" aria-hidden="true" />
        <p className="min-w-0 text-xs sm:text-sm font-medium leading-snug">{item.message}</p>
      </div>
      <div className="flex items-center gap-2 shrink-0 self-end sm:self-center">
        {item.action && (
          <button
            type="button"
            onClick={() => onAction(item)}
            className="h-8 px-2.5 rounded-lg text-[11px] font-bold bg-black/10 dark:bg-white/10 hover:bg-black/15 dark:hover:bg-white/15 transition-colors"
          >
            {item.action.label}
          </button>
        )}
        {item.dismissible && onDismiss && (
          <button
            type="button"
            aria-label="Dismiss announcement"
            onClick={() => onDismiss(item.id)}
            className="h-8 w-8 inline-flex items-center justify-center rounded-lg hover:bg-black/10 dark:hover:bg-white/10"
          >
            <X className="w-4 h-4" aria-hidden="true" />
          </button>
        )}
      </div>
    </div>
  );
};

/** Full-width workspace announcement strip. Renders nothing when there are no visible items. */
export const AnnouncementBar: React.FC = () => {
  const { authFetch, user } = useAuth();
  const billing = useBilling();
  const navigate = useNavigate();
  const { orgSlug } = useParams<{ orgSlug: string }>();
  const pastDue = usePastDueAnnouncement();
  const [remote, setRemote] = useState<AnnouncementItem[]>([]);
  const [dismissed, setDismissed] = useState<Set<string>>(() => new Set());
  const [mobileExpanded, setMobileExpanded] = useState(false);
  const userId = user?.id || '';

  useEffect(() => {
    let mounted = true;
    authFetch('/api/announcements')
      .then(async (res) => (res.ok ? ((await res.json()) as AnnouncementItem[]) : []))
      .then((items) => {
        if (!mounted) return;
        const list = Array.isArray(items) ? items : [];
        setRemote(list);
        setDismissed(
          readDismissedIds(
            typeof localStorage === 'undefined' ? null : localStorage,
            userId,
            list.map((i) => i.id),
          ),
        );
      })
      .catch(() => {
        if (mounted) setRemote([]);
      });
    return () => {
      mounted = false;
    };
  }, [authFetch, userId, orgSlug]);

  const combined = useMemo(
    () => filterVisibleAnnouncements(mergeAnnouncementItems(remote, pastDue), dismissed),
    [remote, pastDue, dismissed],
  );

  const onDismiss = useCallback(
    (id: string) => {
      writeDismissal(typeof localStorage === 'undefined' ? null : localStorage, userId, id);
      setDismissed((prev) => new Set(prev).add(id));
    },
    [userId],
  );

  const onAction = useCallback(
    (item: AnnouncementItem) => {
      const href = item.action?.href;
      if (!href) return;
      if (href === '__portal__') {
        void billing.openPortal().catch(() => undefined);
        return;
      }
      if (href.startsWith('http://') || href.startsWith('https://')) {
        window.location.assign(href);
        return;
      }
      navigate(href);
    },
    [billing, navigate],
  );

  if (combined.length === 0) return null;

  const { first, moreCount } = mobileCollapse(combined);

  return (
    <div className="w-full shrink-0 border-b border-[var(--border-subtle)] bg-[var(--bg-canvas)] z-30" data-testid="announcement-bar">
      <div className="hidden sm:block">
        {combined.map((item) => (
          <AnnouncementRow
            key={item.id}
            item={item}
            onDismiss={item.dismissible ? onDismiss : undefined}
            onAction={onAction}
          />
        ))}
      </div>
      <div className="sm:hidden">
        {first && (
          <AnnouncementRow
            item={first}
            onDismiss={first.dismissible ? onDismiss : undefined}
            onAction={onAction}
          />
        )}
        {moreCount > 0 && !mobileExpanded && (
          <button
            type="button"
            className="w-full text-left px-3 py-1.5 text-[11px] font-semibold text-[var(--text-secondary)] hover:bg-[var(--bg-subtle)]"
            onClick={() => setMobileExpanded(true)}
          >
            +{moreCount} more
          </button>
        )}
        {mobileExpanded &&
          combined.slice(1).map((item) => (
            <AnnouncementRow
              key={item.id}
              item={item}
              onDismiss={item.dismissible ? onDismiss : undefined}
              onAction={onAction}
            />
          ))}
      </div>
    </div>
  );
};

export default AnnouncementBar;
