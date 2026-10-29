import type { BankAccountPickerItem } from '../types/banking';

export interface PaymentBankOption {
  value: string;
  label: string;
}

/**
 * Options for the invoice payment-bank picker. A saved bank that no longer appears in the (active-only) picker
 * is archived or gone; keep it visible and flagged so the admin can see why it needs changing.
 */
export function paymentBankChoice(
  pickerItems: BankAccountPickerItem[],
  selectedId: number | null,
  pickerLoaded = true,
): PaymentBankOption[] {
  // Cash accounts without a bank record come back with id 0; they have no bank details to print.
  const banks = pickerItems.filter((item) => item.id > 0);
  const options: PaymentBankOption[] = [{ value: '', label: 'None, use the default text' }];
  for (const bank of banks) {
    options.push({
      value: String(bank.id),
      label: bank.accountNumberMasked ? `${bank.name} (${bank.accountNumberMasked})` : bank.name,
    });
  }
  if (selectedId !== null && (!pickerLoaded || !banks.some((bank) => bank.id === selectedId))) {
    options.push({
      value: String(selectedId),
      label: pickerLoaded ? 'Archived or unavailable account, pick another' : 'Selected bank account',
    });
  }
  return options;
}
