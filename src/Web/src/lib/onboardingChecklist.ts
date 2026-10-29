export interface ChecklistItem {
  id: 'tax' | 'bank' | 'coa' | 'business' | 'invite';
  label: string;
  hint: string;
  to: string;
  done: boolean;
}

export interface ChecklistInput {
  /** When true, include bank / chart-of-accounts setup (Accounting modules). */
  hasFinancialsModule: boolean;
  taxRegime: string;
  hasLegalName: boolean;
}

/** Items S1–S5 from the onboarding requirements; opening balances (S6) wait until that feature exists. */
export function buildChecklist(input: ChecklistInput): ChecklistItem[] {
  const items: ChecklistItem[] = [];

  if (input.taxRegime !== 'None') {
    items.push({ id: 'tax', label: 'Confirm your tax rates', hint: 'We added a 0% placeholder — set the real rate before your first invoice.', to: 'settings?tab=taxes', done: false });
  }
  if (input.hasFinancialsModule) {
    items.push({ id: 'bank', label: 'Check your bank account', hint: 'Rename the default cash or bank account to match reality.', to: 'financials/accounts', done: false });
    items.push({ id: 'coa', label: 'Skim your chart of accounts', hint: 'Know the control accounts we seeded.', to: 'financials/accounts', done: false });
  }
  items.push({ id: 'business', label: 'Add your legal name and contact', hint: 'Printed in the From block of your invoices.', to: 'settings', done: input.hasLegalName });
  items.push({ id: 'invite', label: 'Invite a teammate', hint: 'Optional — bring your accountant or colleague.', to: 'members', done: false });
  return items;
}
