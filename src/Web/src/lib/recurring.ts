// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

// Shared formatting/labels for the recurring sales-invoice UI (#484).

import type {
  RecurringCadence,
  RecurringScheduleStatus,
  RecurringOccurrenceStatus,
} from '../types/recurring';

type Tone = 'green' | 'amber' | 'red' | 'blue' | 'sky' | 'slate';

export function cadenceLabel(cadence: RecurringCadence, interval: number): string {
  const n = interval > 1 ? interval : 1;
  const unit: Record<RecurringCadence, string> = {
    Weekly: 'week',
    Monthly: 'month',
    Quarterly: 'quarter',
    Yearly: 'year',
  };
  return n === 1 ? `Every ${unit[cadence]}` : `Every ${n} ${unit[cadence]}s`;
}

export function scheduleStatusTone(status: RecurringScheduleStatus): Tone {
  switch (status) {
    case 'Active':
      return 'green';
    case 'Paused':
      return 'amber';
    case 'Ended':
      return 'slate';
    default:
      return 'slate';
  }
}

export function occurrenceStatusTone(status: RecurringOccurrenceStatus): Tone {
  switch (status) {
    case 'Issued':
      return 'green';
    case 'DraftCreated':
      return 'blue';
    case 'Skipped':
      return 'slate';
    case 'Failed':
      return 'red';
    case 'Held':
      return 'amber';
    case 'Running':
    case 'Scheduled':
      return 'sky';
    case 'Superseded':
      return 'slate';
    default:
      return 'slate';
  }
}

export function occurrenceStatusLabel(status: RecurringOccurrenceStatus): string {
  switch (status) {
    case 'DraftCreated':
      return 'Draft created';
    default:
      return status;
  }
}

/** Formats an ISO date (date-only) for display; returns an em dash for null. */
export function formatRunDate(iso?: string | null): string {
  if (!iso) return '—';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '—';
  return d.toLocaleDateString(undefined, { dateStyle: 'medium' });
}

/** Formats an ISO datetime for display; returns an em dash for null. */
export function formatDateTime(iso?: string | null): string {
  if (!iso) return '—';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '—';
  return d.toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' });
}

/** YYYY-MM-DD for a Date, in local time (matches the invoice editor's date inputs). */
export function toDateInput(d: Date): string {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}
