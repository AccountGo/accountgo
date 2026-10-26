const decimal = (value: number) => {
  if (!Number.isFinite(value)) throw new Error('Invoice amounts must be finite numbers.');
  const [mantissa, exponent = '0'] = value.toString().split('e');
  const places = (mantissa.split('.')[1]?.length ?? 0) - Number(exponent);
  return {
    units: BigInt(mantissa.replace('.', '')) * 10n ** BigInt(Math.max(0, -places)),
    scale: 10n ** BigInt(Math.max(0, places)),
  };
};

const round = (numerator: bigint, denominator: bigint) => {
  const sign = numerator < 0n ? -1n : 1n;
  return sign * ((sign * numerator * 2n + denominator) / (denominator * 2n));
};

export interface WorkspaceTax {
  id: number;
  taxCode?: string | null;
  taxName: string;
  rate: number;
  isActive: boolean;
}

/** Active rates other than the seeded "No tax" row. A 0% rate is a real choice. */
export const isSelectableTax = (tax: { taxCode?: string | null; isActive?: boolean }) =>
  tax.isActive !== false && (tax.taxCode ?? '').trim().toUpperCase() !== 'NONE';

export const taxChoiceLabel = (tax: { taxName: string; rate: number; isActive?: boolean }) =>
  tax.isActive === false ? `${tax.taxName} (archived)` : `${tax.taxName} (${tax.rate}%)`;

/** Selectable rates, plus the archived rate already stored on this line. */
export const lineTaxChoices = <T extends { id: number; taxCode?: string | null; isActive?: boolean }>(
  taxes: T[],
  selectedId: number | ''
): T[] => {
  const selectable = taxes.filter(isSelectableTax);
  if (selectedId === '') return selectable;
  const selected = taxes.find((tax) => tax.id === selectedId);
  if (selected && selected.isActive === false && !selectable.some((tax) => tax.id === selected.id))
    return [...selectable, selected];
  return selectable;
};

export const archivedTaxMessage = (taxName: string) =>
  `${taxName} is archived. Pick another tax for this line.`;

export const firstArchivedTax = <T extends { id: number; taxName: string; isActive?: boolean }>(
  taxes: T[],
  selectedIds: Array<number | ''>
): T | undefined =>
  selectedIds
    .map((id) => (id === '' ? undefined : taxes.find((tax) => tax.id === id)))
    .find((tax) => tax != null && tax.isActive === false);

/** Matches the server's decimal arithmetic: round the discounted subtotal, then tax, half away from zero. */
export const invoiceLineAmounts = (quantity: number, price: number, discount: number, taxRate: number) => {
  const q = decimal(quantity);
  const p = decimal(price);
  const d = decimal(discount);
  const t = decimal(taxRate);
  const denominator = q.scale * p.scale * d.scale;
  const subtotalCents = round(q.units * p.units * (100n * d.scale - d.units), denominator);
  const discountCents = round(q.units * p.units * d.units, denominator);
  const taxCents = round(subtotalCents * t.units, 100n * t.scale);
  return {
    subtotal: Number(subtotalCents) / 100,
    discount: Number(discountCents) / 100,
    tax: Number(taxCents) / 100,
  };
};
