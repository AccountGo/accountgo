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
