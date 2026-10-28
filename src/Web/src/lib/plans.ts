/** Mirrors Core.Domain.Company.NormalizePlanKey for cosmetic / ladder UI. */
export function normalizePlanKey(planKey: string | undefined | null): string {
  const key = (planKey ?? '').trim().toLowerCase();
  if (key === 'invoicing') return 'invoicing_free';
  if (key === 'accounting') return 'accounting_free';
  return key;
}

export function isInvoicingProduct(planKey: string | undefined | null): boolean {
  return normalizePlanKey(planKey).startsWith('invoicing');
}

export function isEnterprisePlan(planKey: string | undefined | null): boolean {
  return normalizePlanKey(planKey) === 'enterprise';
}
