/** Journal voucher type labels keyed by JournalVoucherTypes enum value (not array index). */
export const JOURNAL_VOUCHER_TYPE_LABELS: Record<number, string> = {
  1: 'Opening balances',
  2: 'Closing entries',
  3: 'Adjustment entries',
  4: 'Correction entries',
  5: 'Transfer entries',
  6: 'Other',
  7: 'Prior-period adjustment',
};

/** Types a user may select when creating/editing a manual journal (Closing entries are system-only). */
export const SELECTABLE_JOURNAL_VOUCHER_TYPES: { value: number; label: string }[] = [
  { value: 1, label: JOURNAL_VOUCHER_TYPE_LABELS[1] },
  { value: 3, label: JOURNAL_VOUCHER_TYPE_LABELS[3] },
  { value: 4, label: JOURNAL_VOUCHER_TYPE_LABELS[4] },
  { value: 5, label: JOURNAL_VOUCHER_TYPE_LABELS[5] },
  { value: 6, label: JOURNAL_VOUCHER_TYPE_LABELS[6] },
  { value: 7, label: JOURNAL_VOUCHER_TYPE_LABELS[7] },
];

export const PRIOR_PERIOD_ADJUSTMENT = 7;

export function journalVoucherTypeLabel(value: number | null | undefined): string {
  if (value == null) return '';
  return JOURNAL_VOUCHER_TYPE_LABELS[value] ?? `Voucher type ${value}`;
}
