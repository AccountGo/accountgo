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
 * Build IntentStep options from the plan catalog: free choices first, then paid, each in catalog order.
 * Returns [] until the catalog has loaded (caller shows a skeleton). Values are catalog plan keys.
 */
export function buildIntentPlanOptions(
  catalog: PlanCatalogEntry[] | null,
  freeSlot: FreeSlotState = { used: false, freeWorkspaceName: null, freeWorkspaceSlug: null },
): IntentPlanOption[] {
  if (!catalog) return [];

  // Community is the only plan in the self-hosted catalog; it has no checkout.
  if (catalog.length === 1 && catalog[0].key === 'community') {
    const plan = catalog[0];
    return [{ value: plan.key, title: plan.name, description: plan.description || '', free: true,
      product: 'community', monthlyPrice: null, disabled: false }];
  }
  const selfService = catalog.filter((p) => p.selfService);
  const slotUsed = !!freeSlot?.used;

  const freeOptions: IntentPlanOption[] = selfService.filter(isFreePlan).map((plan) => ({
    value: plan.key,
    title: plan.name,
    description: plan.description || '',
    free: true,
    product: (plan.product || '').toLowerCase(),
    monthlyPrice: plan.monthlyPrice,
    disabled: slotUsed,
    badge: plan.badge || undefined,
  }));

  const paidOptions: IntentPlanOption[] = selfService.filter((p) => !isFreePlan(p)).map((plan) => ({
    value: plan.key,
    title: plan.name,
    description:
      plan.description ||
      (plan.monthlyPrice != null ? `$${plan.monthlyPrice}/mo` : 'Paid plan'),
    badge: plan.badge || (plan.monthlyPrice != null ? `$${plan.monthlyPrice}/mo` : undefined),
    free: false,
    product: (plan.product || '').toLowerCase(),
    monthlyPrice: plan.monthlyPrice,
    disabled: false,
  }));

  return [...freeOptions, ...paidOptions];
}

/** The choice to preselect: the highlighted free option, else the first free one; null when none is available. */
export function defaultIntentPlan(options: readonly IntentPlanOption[]): string | null {
  const free = options.filter((option) => option.free && !option.disabled);
  return (free.find((option) => option.badge) ?? free[0])?.value ?? null;
}

export function freeSlotBlockedMessage(name: string | null, hasUpgradeLink = false): string {
  const label = name?.trim() || 'your free workspace';
  // With an upgrade link the next line already says "Upgrade {name} to free up your free slot."
  if (hasUpgradeLink) return `You already have a free workspace (${label}). Choose a paid plan for this one.`;
  return `You already have a free workspace (${label}). Choose a paid plan for this one, or upgrade ${label} to free up your free slot.`;
}

/** Plan-step error when the free slot is used and a free choice is still selected; null when the choice is fine. */
export function freeSlotPlanError(plan: string, catalog: PlanCatalogEntry[] | null, freeSlot: FreeSlotState): string | null {
  if (!freeSlot.used || !catalog || (catalog.length === 1 && catalog[0].key === 'community')) return null;
  return isPaidWizardPlan(plan, catalog) ? null : 'Choose a paid plan to continue.';
}

export function isPaidWizardPlan(plan: string, catalog: PlanCatalogEntry[] | null): boolean {
  if (!catalog) return false;
  if (plan === 'community' && catalog.length === 1) return false;
  const entry = catalog.find((p) => p.key === plan);
  return !!entry && !isFreePlan(entry);
}
