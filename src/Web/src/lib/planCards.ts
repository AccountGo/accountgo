/** Catalog row shape needed to derive subscription plan cards. */
export interface PlanCardCatalogEntry {
  key: string;
  name: string;
  description: string;
  selfService: boolean;
  monthlyPrice: number | null;
}

export type PlanCardRole = 'current' | 'action';

export interface DerivedPlanCard {
  key: string;
  role: PlanCardRole;
  name: string;
  description: string;
  monthlyPrice: number | null;
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
      actionLabel: isUpgrade ? 'Upgrade' : 'Switch',
    });
  }

  return cards;
}
