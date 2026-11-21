// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

// Types for the recurring sales-invoice schedules feature (#484). Mirror the API DTOs in
// Services/Recurring/RecurringScheduleModels.cs. Enums are sent/received as their string names.

export type RecurringScheduleStatus = 'Active' | 'Paused' | 'Ended';
export type RecurringCadence = 'Weekly' | 'Monthly' | 'Quarterly' | 'Yearly';
export type RecurringAction = 'CreateDraft' | 'AutoIssue' | 'AutoApprove';
export type RecurringServicePeriodMode = 'CurrentMonth' | 'PreviousMonth';
export type RecurringDiscountType = 'Percentage' | 'Amount';

export type RecurringOccurrenceStatus =
  | 'Scheduled'
  | 'Running'
  | 'DraftCreated'
  | 'Issued'
  | 'Skipped'
  | 'Failed'
  | 'Held'
  | 'Superseded';

export interface RecurringScheduleSummary {
  id: string;
  name: string;
  customerId: string;
  customerName?: string | null;
  customerArchived: boolean;
  status: RecurringScheduleStatus;
  cadence: RecurringCadence;
  interval: number;
  action: RecurringAction;
  nextRunDate?: string | null;
  startDate: string;
  endDate?: string | null;
  maxOccurrences?: number | null;
  occurrenceCount: number;
  needsAttention: boolean;
  consecutiveFailures: number;
  isOverdue: boolean;
}

export interface RecurringScheduleLineView {
  id: string;
  itemId?: string | null;
  itemNo?: string | null;
  description?: string | null;
  quantity: number;
  amount: number;
  discount: number;
  discountType: RecurringDiscountType;
  taxId?: string | null;
  accountId?: string | null;
  sortOrder: number;
}

export interface RecurringScheduleDetail {
  id: string;
  name: string;
  customerId: string;
  customerName?: string | null;
  customerArchived: boolean;
  status: RecurringScheduleStatus;
  cadence: RecurringCadence;
  interval: number;
  dayOfMonth?: number | null;
  startDate: string;
  endDate?: string | null;
  maxOccurrences?: number | null;
  nextRunDate?: string | null;
  occurrenceCount: number;
  action: RecurringAction;
  servicePeriodMode: RecurringServicePeriodMode;
  paymentTermId?: string | null;
  notes?: string | null;
  paymentInstructions?: string | null;
  needsAttention: boolean;
  consecutiveFailures: number;
  isOverdue: boolean;
  lines: RecurringScheduleLineView[];
  upcomingDates: string[];
}

export interface RecurringOccurrenceView {
  id: string;
  recurringScheduleId: string;
  scheduledRunDate: string;
  periodStart: string;
  periodEnd: string;
  documentDate: string;
  status: RecurringOccurrenceStatus;
  salesInvoiceId?: string | null;
  errorCode?: string | null;
  guardFailureReason?: string | null;
  skipReason?: string | null;
  actionBy?: string | null;
  attemptCount: number;
  updatedAt?: string | null;
  createdAt: string;
}

/** A line as the editor edits it (form state). */
export interface RecurringLineInput {
  id?: string;
  itemId?: string | null;
  description?: string;
  quantity: number;
  amount: number;
  discount: number;
  discountType: RecurringDiscountType;
  taxId?: string | '';
  accountId?: string | null;
  sortOrder?: number;
}

/** A line as sent to the API (matches RecurringScheduleLineInput; taxId is nullable, not ''). */
export interface RecurringLinePayload {
  id?: string;
  itemId?: string | null;
  description?: string | null;
  quantity: number;
  amount: number;
  discount: number;
  discountType: RecurringDiscountType;
  taxId?: string | null;
  accountId?: string | null;
  sortOrder?: number;
}

/** Create/update payload (matches RecurringScheduleInput). */
export interface RecurringScheduleInput {
  id?: string;
  customerId: string;
  name: string;
  cadence: RecurringCadence;
  interval: number;
  dayOfMonth?: number | null;
  startDate: string;
  endDate?: string | null;
  maxOccurrences?: number | null;
  action: RecurringAction;
  servicePeriodMode: RecurringServicePeriodMode;
  paymentTermId?: string | null;
  notes?: string | null;
  paymentInstructions?: string | null;
  lines: RecurringLinePayload[];
}
