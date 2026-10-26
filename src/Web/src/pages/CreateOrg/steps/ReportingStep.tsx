import React from 'react';
import {
  basisForFramework,
  effectiveInventoryMethod,
  inventoryOptions,
  inventoryRelevant,
  type IndustryTemplate,
  type InventoryCostingMethod,
  type ReportingFramework,
  type TaxRegime,
  type WizardState,
} from '../lib/policyDefaults';
import { ChoiceCards, type ChoiceOption } from './ui';

const FRAMEWORKS: ChoiceOption<ReportingFramework>[] = [
  { value: 'IfrsForSmes', title: 'IFRS for SMEs', description: 'Simplified IFRS for private companies outside the US.', badge: 'Recommended for most' },
  { value: 'UsGaapPrivate', title: 'US GAAP (private)', description: 'For US private companies reporting under FASB ASC.', badge: 'Recommended for US' },
  { value: 'Ifrs', title: 'Full IFRS', description: 'For groups or lenders that require full IFRS statements.' },
  { value: 'CashBasis', title: 'Cash basis', description: 'Record income and expenses when cash moves. Not GAAP or IFRS.' },
];

const INVENTORY_LABELS: Record<InventoryCostingMethod, ChoiceOption<InventoryCostingMethod>> = {
  None: { value: 'None', title: 'None', description: 'No inventory costing.' },
  WeightedAverage: { value: 'WeightedAverage', title: 'Weighted average', description: 'Average cost of units on hand.', badge: 'Default' },
  Fifo: { value: 'Fifo', title: 'FIFO', description: 'Oldest stock is sold first.' },
};

export const ReportingStep: React.FC<{
  state: WizardState;
  update: (patch: Partial<WizardState>) => void;
}> = ({ state, update }) => {
  const frameworkControl = (
    <div className="space-y-3">
      <ChoiceCards<ReportingFramework>
        legend="Reporting framework"
        name="reportingFramework"
        value={state.reportingFramework}
        onChange={(reportingFramework) => update({ reportingFramework })}
        options={FRAMEWORKS}
      />
      <p className="text-xs text-[var(--text-secondary)]">
        Accounting basis: <strong className="text-[var(--text-main)]">{basisForFramework(state.reportingFramework)}</strong>
        {state.reportingFramework === 'CashBasis'
          ? ' — cash basis reporting always uses the cash basis.'
          : ' — this framework requires accrual accounting.'}
      </p>
    </div>
  );

  return (
    <div className="space-y-5">
      {state.plan === 'invoicing' ? (
        <details className="rounded-xl bg-[var(--bg-subtle)] p-3.5">
          <summary className="text-sm font-semibold text-[var(--text-main)] cursor-pointer">
            Reporting framework: {FRAMEWORKS.find((item) => item.value === state.reportingFramework)?.title}
            <span className="font-normal text-[var(--text-muted)]"> — change</span>
          </summary>
          <div className="mt-3">{frameworkControl}</div>
        </details>
      ) : frameworkControl}

      <ChoiceCards<TaxRegime>
        legend="Tax posture"
        name="taxRegime"
        value={state.taxRegime}
        onChange={(taxRegime) => update({ taxRegime })}
        columns="sm:grid-cols-3"
        options={[
          { value: 'None', title: 'No tax', description: 'You do not charge sales tax or VAT.' },
          { value: 'SalesTax', title: 'Sales tax', description: 'You charge sales tax. Set rates after setup.' },
          { value: 'VatGst', title: 'VAT / GST', description: 'You charge VAT or GST. Set rates after setup.' },
        ]}
      />

      <ChoiceCards<IndustryTemplate>
        legend="What do you sell?"
        name="industryTemplate"
        value={state.industryTemplate}
        onChange={(industryTemplate) => update({ industryTemplate })}
        columns="sm:grid-cols-3"
        options={[
          { value: 'Services', title: 'Services', description: 'Time, projects and subscriptions.' },
          { value: 'ProductRetail', title: 'Products / retail', description: 'Goods you buy and resell or make.' },
          { value: 'Mixed', title: 'Mixed', description: 'Both products and services.' },
        ]}
      />

      {inventoryRelevant(state) && (
        <ChoiceCards<InventoryCostingMethod>
          legend="Inventory costing"
          name="inventoryCostingMethod"
          value={effectiveInventoryMethod(state)}
          onChange={(inventoryCostingMethod) => update({ inventoryCostingMethod })}
          columns="sm:grid-cols-2"
          options={inventoryOptions().map((method) => INVENTORY_LABELS[method])}
        />
      )}
    </div>
  );
};
