/** Minimal plan shape these helpers read; PlanCatalogEntry satisfies it. */
export interface PlanFeatureSource {
  name: string;
  selfService: boolean;
  features?: string[];
}

/** Plan shape for module and product-group lookups; PlanCatalogEntry satisfies it. */
export interface PlanModuleSource {
  key: string;
  name: string;
  selfService: boolean;
  includedModules?: readonly string[];
  product?: string | null;
  productLabel?: string | null;
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

/** First self-service plan, in catalog order, that includes the module; null when none does. */
export function firstPlanWithModule<T extends PlanModuleSource>(plans: readonly T[] | null | undefined, moduleKey: string): T | null {
  return (plans ?? []).find((plan) => plan.selfService && (plan.includedModules ?? []).includes(moduleKey)) ?? null;
}

/** True when some plan in the list includes the module. */
export function anyPlanIncludesModule(plans: readonly PlanModuleSource[], moduleKey: string): boolean {
  return plans.some((plan) => (plan.includedModules ?? []).includes(moduleKey));
}

export interface ProductGroup {
  product: string;
  label: string;
}

/** Distinct product groups in catalog order, labelled by the catalog (falls back to the group key). */
export function productGroups(plans: readonly PlanModuleSource[] | null | undefined): ProductGroup[] {
  const groups: ProductGroup[] = [];
  for (const plan of plans ?? []) {
    const product = plan.product?.trim();
    if (!product || groups.some((group) => group.product === product)) continue;
    groups.push({ product, label: plan.productLabel?.trim() || product });
  }
  return groups;
}
