import type { PlanCatalogEntry } from './usePlanCatalog';

export interface IntentPlanOption {
  value: string;
  title: string;
  description: string;
  badge?: string;
  disabled?: boolean;
  /** True when monthlyPrice === 0. */
  free: boolean;
  product: string;
  monthlyPrice: number | null;
}

export interface FreeSlotState {
  /** True when limit !== null && used >= limit. */
  used: boolean;
  freeWorkspaceName: string | null;
  freeWorkspaceSlug: string | null;
}

export function isFreePlan(entry: PlanCatalogEntry): boolean {
  return entry.monthlyPrice === 0;
}

export function isFreeSlotUsed(used: number | null | undefined, limit: number | null | undefined): boolean {
  return limit != null && used != null && used >= limit;
}

/**
 * Build IntentStep options from the plan catalog.
 * Returns [] until the catalog has loaded (caller shows a skeleton).
 * Free choices keep product aliases ("invoicing" / "accounting"); paid choices use catalog keys.
 */
export function buildIntentPlanOptions(
  catalog: PlanCatalogEntry[] | null,
  freeSlot: FreeSlotState = { used: false, freeWorkspaceName: null, freeWorkspaceSlug: null },
): IntentPlanOption[] {
  if (!catalog) return [];

  const selfService = catalog.filter((p) => p.selfService);
  const free = selfService.filter(isFreePlan);
  const paid = selfService.filter((p) => !isFreePlan(p));
  const slotUsed = !!freeSlot?.used;

  const freeOptions: IntentPlanOption[] = free.map((plan) => {
    const product = (plan.product || '').toLowerCase();
    const alias = product === 'invoicing' ? 'invoicing' : product === 'accounting' ? 'accounting' : plan.key;
    return {
      value: alias,
      title: plan.name,
      description: plan.description || '',
      free: true,
      product: product || alias,
      monthlyPrice: plan.monthlyPrice,
      disabled: slotUsed,
      badge: alias === 'accounting' ? 'Most popular' : undefined,
    };
  });

  // Deduplicate free aliases (catalog may list both product free keys).
  const seenFree = new Set<string>();
  const uniqueFree = freeOptions.filter((opt) => {
    if (seenFree.has(opt.value)) return false;
    seenFree.add(opt.value);
    return true;
  });

  const paidOptions: IntentPlanOption[] = paid
    .slice()
    .sort((a, b) => (a.monthlyPrice ?? 0) - (b.monthlyPrice ?? 0))
    .map((plan) => ({
      value: plan.key,
      title: plan.name,
      description:
        plan.description ||
        (plan.monthlyPrice != null ? `$${plan.monthlyPrice}/mo` : 'Paid plan'),
      badge: plan.monthlyPrice != null ? `$${plan.monthlyPrice}/mo` : undefined,
      free: false,
      product: (plan.product || '').toLowerCase(),
      monthlyPrice: plan.monthlyPrice,
      disabled: false,
    }));

  // Free first, then paid; keep product grouping (invoicing then accounting) via stable sort.
  const productOrder = (p: string) => (p === 'invoicing' ? 0 : p === 'accounting' ? 1 : 2);
  uniqueFree.sort((a, b) => productOrder(a.product) - productOrder(b.product));
  paidOptions.sort(
    (a, b) => productOrder(a.product) - productOrder(b.product) || (a.monthlyPrice ?? 0) - (b.monthlyPrice ?? 0),
  );

  return [...uniqueFree, ...paidOptions];
}

export function freeSlotBlockedMessage(name: string | null, hasUpgradeLink = false): string {
  const label = name?.trim() || 'your free workspace';
  // With an upgrade link the next line already says "Upgrade {name} to free up your free slot."
  if (hasUpgradeLink) return `You already have a free workspace (${label}). Choose a paid plan for this one.`;
  return `You already have a free workspace (${label}). Choose a paid plan for this one, or upgrade ${label} to free up your free slot.`;
}

/** Plan-step error when the free slot is used and a free choice is still selected; null when the choice is fine. */
export function freeSlotPlanError(plan: string, catalog: PlanCatalogEntry[] | null, freeSlot: FreeSlotState): string | null {
  if (!freeSlot.used || !catalog) return null;
  return isPaidWizardPlan(plan, catalog) ? null : 'Choose a paid plan to continue.';
}

export function isPaidWizardPlan(plan: string, catalog: PlanCatalogEntry[] | null): boolean {
  if (!catalog) return false;
  if (plan === 'invoicing' || plan === 'accounting') return false;
  const entry = catalog.find((p) => p.key === plan);
  return !!entry && !isFreePlan(entry);
}
