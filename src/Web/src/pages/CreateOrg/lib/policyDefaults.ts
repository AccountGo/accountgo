import type { FiscalYearPattern } from './fyPreview';

export type { FiscalYearPattern };
export type Plan = 'invoicing' | 'accounting' | 'enterprise';
export type ReportingFramework = 'IfrsForSmes' | 'UsGaapPrivate' | 'Ifrs' | 'CashBasis';
export type AccountingBasis = 'Accrual' | 'Cash';
export type TaxRegime = 'None' | 'SalesTax' | 'VatGst';
export type IndustryTemplate = 'Services' | 'ProductRetail' | 'Mixed';
export type InventoryCostingMethod = 'None' | 'Fifo' | 'WeightedAverage';
export type WorkspacePurpose = 'Personal' | 'Business';
export type CollaborationIntent = 'JustMe' | 'WithOthers';

export interface WizardState {
  workspacePurpose: WorkspacePurpose | '';
  collaborationIntent: CollaborationIntent | '';
  plan: Plan;
  name: string;
  slug: string;
  slugTouched: boolean;
  legalName: string;
  currency: string;
  booksStartDate: string;
  fiscalYearPattern: FiscalYearPattern;
  fiscalYearStartMonth: number | null;
  reportingFramework: ReportingFramework;
  taxRegime: TaxRegime;
  industryTemplate: IndustryTemplate;
  inventoryCostingMethod: InventoryCostingMethod;
}

export type StepId = 'about' | 'intent' | 'identity' | 'moneyTime' | 'reporting' | 'review';
export const STEPS: StepId[] = ['about', 'intent', 'identity', 'moneyTime', 'reporting', 'review'];

// Continuation mode initializes an EXISTING signup-created workspace. Its plan, identity, currency, books,
// framework, tax, industry and inventory are already persisted and the continuation endpoint accepts none
// of them — it applies only the About answers (purpose/collaboration → access + journal workflow). So the
// continuation wizard presents ONLY the About step and a read-only Review of the stored workspace; it never
// asks the user to edit — nor validates — fields it would silently discard. Plan changes happen afterward
// through the billing flow.
export const CONTINUATION_STEPS: StepId[] = ['about', 'review'];

/** The ordered step list for the current mode. */
export const stepsFor = (isContinuation: boolean): StepId[] =>
  isContinuation ? CONTINUATION_STEPS : STEPS;

export const CURRENCIES = ['USD', 'PHP', 'EUR', 'GBP', 'CAD', 'AUD', 'SGD', 'JPY', 'INR', 'IDR', 'MYR', 'THB', 'VND', 'NZD', 'HKD', 'CNY'];

export const slugify = (value: string): string =>
  value
    .toLowerCase()
    .replace(/[^a-z0-9-]+/g, '-')
    .replace(/-+/g, '-')
    .replace(/^-+|-+$/g, '');

export const isoDate = (date: Date): string => {
  const pad = (value: number) => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
};

export const defaultState = (today: Date = new Date(), prefill: { name?: string; slug?: string } = {}): WizardState => {
  const name = (prefill.name ?? '').trim();
  return {
    workspacePurpose: '',
    collaborationIntent: '',
    plan: 'accounting',
    name,
    slug: slugify(prefill.slug || name),
    slugTouched: Boolean(prefill.slug),
    legalName: '',
    currency: 'USD',
    booksStartDate: isoDate(today),
    fiscalYearPattern: 'Calendar',
    fiscalYearStartMonth: null,
    reportingFramework: 'IfrsForSmes',
    taxRegime: 'None',
    industryTemplate: 'Services',
    inventoryCostingMethod: 'None',
  };
};

/** Merge a possibly older draft into current defaults without inventing About answers. */
export const normalizeWizardState = (raw: Partial<WizardState> | null | undefined, today: Date = new Date()): WizardState => {
  const base = defaultState(today);
  if (!raw || typeof raw !== 'object') return base;
  const purpose = raw.workspacePurpose === 'Personal' || raw.workspacePurpose === 'Business' ? raw.workspacePurpose : '';
  const collaboration = raw.collaborationIntent === 'JustMe' || raw.collaborationIntent === 'WithOthers'
    ? raw.collaborationIntent
    : '';
  return {
    ...base,
    ...raw,
    workspacePurpose: purpose,
    collaborationIntent: collaboration,
  };
};

export interface SetupCapabilitiesPayloadOptions {
  soloSetupEnabled?: boolean;
}

/** First step that still needs a required answer (not a numeric index). */
export const firstIncompleteStepIndex = (
  state: WizardState,
  today: Date = new Date(),
  capabilities?: SetupCapabilitiesPayloadOptions | null,
  isContinuation = false,
): number => {
  const steps = stepsFor(isContinuation);
  for (let index = 0; index < steps.length; index++) {
    const step = steps[index];
    if (step === 'review') return index;
    if (Object.keys(validateStep(step, state, today, capabilities)).length > 0) return index;
  }
  return steps.indexOf('review');
};

// Cash basis is its own framework; every other framework is accrual (requirements 4.3).
export const basisForFramework = (framework: ReportingFramework): AccountingBasis =>
  framework === 'CashBasis' ? 'Cash' : 'Accrual';

export const inventoryRelevant = (state: Pick<WizardState, 'plan' | 'industryTemplate'>): boolean =>
  state.industryTemplate !== 'Services' || state.plan === 'enterprise';

// Inventory choices never include LIFO, which IFRS and IFRS for SMEs do not permit.
export const inventoryOptions = (): InventoryCostingMethod[] => ['WeightedAverage', 'Fifo'];

export const effectiveInventoryMethod = (state: WizardState): InventoryCostingMethod =>
  inventoryRelevant(state)
    ? (state.inventoryCostingMethod === 'None' ? 'WeightedAverage' : state.inventoryCostingMethod)
    : 'None';

export interface StepErrors {
  [field: string]: string;
}

export const validateStep = (
  step: StepId,
  state: WizardState,
  today: Date = new Date(),
  capabilities?: SetupCapabilitiesPayloadOptions | null,
): StepErrors => {
  const errors: StepErrors = {};
  const soloEnabled = capabilities?.soloSetupEnabled !== false;
  if (step === 'about' && soloEnabled) {
    if (state.workspacePurpose !== 'Personal' && state.workspacePurpose !== 'Business') {
      errors.workspacePurpose = 'Choose what you will use this workspace for.';
    }
    if (state.collaborationIntent !== 'JustMe' && state.collaborationIntent !== 'WithOthers') {
      errors.collaborationIntent = 'Choose who will use this workspace.';
    }
  }
  if (step === 'identity') {
    if (!state.name.trim()) errors.name = 'Workspace name is required.';
    const slug = slugify(state.slug);
    if (!slug) errors.slug = 'Enter a URL slug using letters, numbers or hyphens.';
    else if (slug.length > 100) errors.slug = 'The URL slug must be 100 characters or fewer.';
  }
  if (step === 'moneyTime') {
    if (!/^[A-Z]{3}$/.test(state.currency.trim().toUpperCase())) errors.currency = 'Choose a three-letter currency code.';
    const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(state.booksStartDate);
    const parsed = match ? new Date(Date.UTC(Number(match[1]), Number(match[2]) - 1, Number(match[3]))) : null;
    const valid = parsed !== null && !Number.isNaN(parsed.getTime())
      && parsed.getUTCFullYear() === Number(match![1]) && parsed.getUTCMonth() === Number(match![2]) - 1
      && parsed.getUTCDate() === Number(match![3]);
    if (!valid) {
      errors.booksStartDate = 'Enter a valid books start date.';
    } else {
      const limit = Date.UTC(today.getFullYear() + 1, today.getMonth(), today.getDate());
      if (Number(match![1]) < 1900 || parsed!.getTime() > limit) {
        errors.booksStartDate = 'Books start must be after 1900 and no more than a year from today.';
      }
    }
    if (state.fiscalYearPattern === 'Custom'
      && !(state.fiscalYearStartMonth !== null && Number.isInteger(state.fiscalYearStartMonth)
        && state.fiscalYearStartMonth >= 1 && state.fiscalYearStartMonth <= 12)) {
      errors.fiscalYearStartMonth = 'Choose the month your fiscal year starts.';
    }
  }
  return errors;
};

export const validateAll = (
  state: WizardState,
  today: Date = new Date(),
  capabilities?: SetupCapabilitiesPayloadOptions | null,
  isContinuation = false,
): StepErrors =>
  // Continuation validates ONLY the About answers — identity/moneyTime are the existing workspace's stored,
  // read-only settings, which the continuation neither edits nor sends, so validating them would block the
  // flow on fields the user cannot change here.
  isContinuation
    ? { ...validateStep('about', state, today, capabilities) }
    : {
        ...validateStep('about', state, today, capabilities),
        ...validateStep('identity', state, today, capabilities),
        ...validateStep('moneyTime', state, today, capabilities),
      };

export const buildPayload = (state: WizardState, capabilities?: SetupCapabilitiesPayloadOptions | null) => {
  const soloEnabled = capabilities?.soloSetupEnabled !== false;
  return {
    name: state.name.trim(),
    slug: slugify(state.slug || state.name),
    plan: state.plan,
    currency: state.currency.trim().toUpperCase(),
    legalName: state.legalName.trim() || undefined,
    reportingFramework: state.reportingFramework,
    accountingBasis: basisForFramework(state.reportingFramework),
    fiscalYearPattern: state.fiscalYearPattern,
    fiscalYearStartMonth: state.fiscalYearPattern === 'Custom' ? state.fiscalYearStartMonth : null,
    booksStartDate: state.booksStartDate,
    taxRegime: state.taxRegime,
    industryTemplate: state.industryTemplate,
    inventoryCostingMethod: effectiveInventoryMethod(state),
    workspacePurpose: soloEnabled ? (state.workspacePurpose || undefined) : undefined,
    collaborationIntent: soloEnabled ? (state.collaborationIntent || undefined) : undefined,
    // Personal purpose always uses the Individual accounting profile. Sending it here keeps Create and
    // signup-continuation provisioning aligned with initialization; the server also defaults when personal
    // is released, but the wizard must not rely on Sole Proprietor as a silent fallback.
    entityType: soloEnabled && state.workspacePurpose === 'Personal' ? 'Individual' : undefined,
    ownerAccessPreset: soloEnabled && (state.collaborationIntent === 'JustMe' || state.collaborationIntent === 'WithOthers')
      ? 'FullWorkspaceAccess'
      : undefined,
    initialJournalWorkflow: soloEnabled && state.collaborationIntent === 'JustMe' ? 'SingleActorAllowed' : undefined,
  };
};
