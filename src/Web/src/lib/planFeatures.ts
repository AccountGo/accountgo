/** Minimal plan shape these helpers read; PlanCatalogEntry satisfies it. */
export interface PlanFeatureSource {
  name: string;
  selfService: boolean;
  features?: string[];
}

/** Names of the self-service plans whose catalog features include the feature, in catalog order. */
export function planNamesWithFeature(plans: readonly PlanFeatureSource[] | null | undefined, feature: string): string[] {
  return (plans ?? [])
    .filter((plan) => plan.selfService && (plan.features ?? []).includes(feature))
    .map((plan) => plan.name);
}

/** "A", "A and B", "A, B and C". */
export function formatPlanList(names: readonly string[]): string {
  if (names.length <= 1) return names[0] ?? '';
  return `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]}`;
}

/** "Available on A and B." from the catalog, or a generic sentence when no plan lists the feature. */
export function featureAvailabilityText(plans: readonly PlanFeatureSource[] | null | undefined, feature: string): string {
  const names = planNamesWithFeature(plans, feature);
  return names.length > 0 ? `Available on ${formatPlanList(names)}.` : 'Available on a higher plan.';
}
