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
  /** At least one active bank account exists. */
  hasBankAccount?: boolean;
}

/** Items S1–S5 from the onboarding requirements. */
export function buildChecklist(input: ChecklistInput): ChecklistItem[] {
  const items: ChecklistItem[] = [];

  if (input.taxRegime !== 'None') {
    items.push({ id: 'tax', label: 'Confirm your tax rates', hint: 'We added a 0% placeholder — set the real rate before your first invoice.', to: 'settings?tab=taxes', done: false });
  }
  if (input.hasFinancialsModule) {
    items.push({
      id: 'bank',
      label: 'Add your bank account',
      hint: input.hasBankAccount
        ? 'You can set opening balances on Banking when you take over books.'
        : 'Name the accounts you deposit into and pay from.',
      to: 'banking',
      done: !!input.hasBankAccount,
    });
    items.push({ id: 'coa', label: 'Skim your chart of accounts', hint: 'Know the control accounts we seeded.', to: 'financials/accounts', done: false });
  }
  items.push({ id: 'business', label: 'Add your legal name and contact', hint: 'Printed in the From block of your invoices.', to: 'settings', done: input.hasLegalName });
  items.push({ id: 'invite', label: 'Invite a teammate', hint: 'Optional — bring your accountant or colleague.', to: 'members', done: false });
  return items;
}
