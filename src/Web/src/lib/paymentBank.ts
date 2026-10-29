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

/** First line of the text the server builds from a bank account (mirrors BankPaymentInstructions.Heading). */
export const GENERATED_BANK_INSTRUCTIONS_HEADING = 'Pay by bank transfer to:';

/**
 * What to send for an invoice's payment instructions. Text is sent as typed. A field the user cleared on purpose is
 * sent as an empty string so the invoice prints nothing; a field never touched is left out so the workspace's payment
 * bank or default text applies.
 */
export function paymentInstructionsPayload(text: string, clearedOnPurpose: boolean): string | undefined {
  const trimmed = text.trim();
  if (trimmed) return trimmed;
  return clearedOnPurpose ? '' : undefined;
}

/**
 * Duplicating an issued invoice: the stored text may be the bank details frozen at issue, with the old invoice number
 * in its Reference line. That text is not carried over; the new draft resolves the bank details with its own number.
 */
export function carriedOverPaymentInstructions(stored: string | null | undefined): string {
  if (!stored) return '';
  return stored.trimStart().startsWith(GENERATED_BANK_INSTRUCTIONS_HEADING) ? '' : stored;
}
