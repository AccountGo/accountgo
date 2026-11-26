// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

export type BillSourceDocumentType = 'unspecified' | 'supplier-invoice' | 'expense-receipt';

export const BILL_SOURCE_DOCUMENT_OPTIONS: ReadonlyArray<{
  value: BillSourceDocumentType;
  label: string;
}> = [
  { value: 'unspecified', label: 'Not specified' },
  { value: 'supplier-invoice', label: 'Supplier invoice' },
  { value: 'expense-receipt', label: 'Expense receipt' },
];

export function normalizeBillSourceDocumentType(value: string | null | undefined): BillSourceDocumentType {
  if (value === 'supplier-invoice' || value === 'expense-receipt') return value;
  return 'unspecified';
}

export function billSourceDocumentLabel(value: string | null | undefined): string {
  switch (normalizeBillSourceDocumentType(value)) {
    case 'supplier-invoice':
      return 'Supplier invoice';
    case 'expense-receipt':
      return 'Expense receipt';
    default:
      return 'Not specified';
  }
}

/**
 * Central mapping from document-capture vendor kinds (`purchase-invoice` / `expense-receipt`)
 * to bill-level `SourceDocumentType` wire values (`supplier-invoice` / `expense-receipt`).
 */
export function mapCaptureKindToBillSourceDocument(kind: string | null | undefined): BillSourceDocumentType | null {
  if (kind === 'purchase-invoice') return 'supplier-invoice';
  if (kind === 'expense-receipt') return 'expense-receipt';
  return null;
}
