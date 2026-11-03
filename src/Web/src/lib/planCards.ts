/** Catalog row shape needed to derive subscription plan cards. */
export interface PlanCardCatalogEntry {
  key: string;
  name: string;
  description: string;
  selfService: boolean;
  monthlyPrice: number | null;
  annualPrice?: number | null;
}

export type PlanCardRole = 'current' | 'action';

export function plansForProduct<T extends { product?: string | null }>(plans: T[], product: string): T[] {
  return plans.filter(plan => plan.product === product);
}

/** Only offer a transition when its advertised capabilities resolve the denial. */
export function eligibleUpgradePlans<T extends { key: string; selfService: boolean; includedModules?: string[]; features?: string[]; limits?: Record<string, number | null> }>(
  plans: T[], allowed: string[], problem: { code: string; requiredFeature?: string; requiredModule?: string; resource?: string; used?: number },
): T[] {
  return plans.filter(plan => {
    if (!plan.selfService || !allowed.includes(plan.key)) return false;
    if (problem.code === 'FEATURE_NOT_ENTITLED') return !!problem.requiredFeature && !!plan.features?.includes(problem.requiredFeature);
    if (problem.code === 'MODULE_NOT_SUBSCRIBED') return !!problem.requiredModule && !!plan.includedModules?.includes(problem.requiredModule);
    if (problem.code === 'QUOTA_EXCEEDED') {
      if (!problem.resource || !plan.limits || !Object.hasOwn(plan.limits, problem.resource) || problem.used == null) return false;
      const limit = plan.limits[problem.resource];
      return limit === null || limit > problem.used;
    }
    return false;
  });
}

/** Annual prices are not provided by the catalog; do not imply a monthly price is annual. */
export function planPriceLabel(monthlyPrice: number | null, interval: 'month' | 'year', annualPrice?: number | null): string | null {
  if (monthlyPrice == null) return null;
  if (monthlyPrice === 0) return '$0';
  return interval === 'month' ? `$${monthlyPrice}/mo` : annualPrice != null ? `$${annualPrice}/yr` : null;
}

export interface DerivedPlanCard {
  key: string;
  role: PlanCardRole;
  name: string;
  description: string;
  monthlyPrice: number | null;
  annualPrice: number | null;
  /** Empty on the current-plan card; otherwise Upgrade or Switch. */
  actionLabel: '' | 'Upgrade' | 'Switch';
}

/**
 * Cards = the current plan, plus each allowedTransitions key that exists in the
 * catalog with selfService. Labels compare monthlyPrice only (null treated as 0).
 */
export function derivePlanCards(
  currentPlanKey: string,
  allowedTransitions: string[],
  catalog: PlanCardCatalogEntry[],
): DerivedPlanCard[] {
  const byKey = new Map(catalog.map((plan) => [plan.key, plan]));
  const current = byKey.get(currentPlanKey);
  const currentPrice = current?.monthlyPrice ?? null;
  const cards: DerivedPlanCard[] = [
    {
      key: currentPlanKey,
      role: 'current',
      name: current?.name ?? currentPlanKey,
      description: current?.description ?? '',
      monthlyPrice: currentPrice,
      annualPrice: current?.annualPrice ?? null,
      actionLabel: '',
    },
  ];

  for (const transitionKey of allowedTransitions) {
    if (transitionKey === currentPlanKey) continue;
    const plan = byKey.get(transitionKey);
    if (!plan || !plan.selfService) continue;
    const targetPrice = plan.monthlyPrice;
    const isUpgrade = (targetPrice ?? 0) > (currentPrice ?? 0);
    cards.push({
      key: plan.key,
      role: 'action',
      name: plan.name,
      description: plan.description,
      monthlyPrice: targetPrice,
      annualPrice: plan.annualPrice ?? null,
      actionLabel: isUpgrade ? 'Upgrade' : 'Switch',
    });
  }

  return cards;
}
