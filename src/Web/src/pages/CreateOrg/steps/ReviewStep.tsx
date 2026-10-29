import React from 'react';
import { fiscalYearPreview } from '../lib/fyPreview';
import { basisForFramework, effectiveInventoryMethod, inventoryRelevant, type WizardState } from '../lib/policyDefaults';

const PLAN_LABEL = { invoicing: 'Invoicing', accounting: 'Full accounting', enterprise: 'Enterprise' } as const;
const FRAMEWORK_LABEL = { IfrsForSmes: 'IFRS for SMEs', UsGaapPrivate: 'US GAAP (private)', Ifrs: 'Full IFRS', CashBasis: 'Cash basis' } as const;
const TAX_LABEL = { None: 'No tax', SalesTax: 'Sales tax', VatGst: 'VAT / GST' } as const;
const INDUSTRY_LABEL = { Services: 'Services', ProductRetail: 'Products / retail', Mixed: 'Mixed' } as const;
const INVENTORY_LABEL = { None: 'None', WeightedAverage: 'Weighted average', Fifo: 'FIFO' } as const;

export const ReviewStep: React.FC<{ state: WizardState }> = ({ state }) => {
  const preview = fiscalYearPreview(state.booksStartDate, state.fiscalYearPattern, state.fiscalYearStartMonth);
  const rows: [string, string][] = [
    ['Plan', PLAN_LABEL[state.plan]],
    ['Workspace', state.name.trim()],
    ['URL', `/org/${state.slug}`],
    ...(state.legalName.trim() ? [['Legal name', state.legalName.trim()] as [string, string]] : []),
    ['Currency', state.currency.toUpperCase()],
    ['Books start', state.booksStartDate],
    ['First fiscal year', preview?.label ?? '—'],
    ['Framework', `${FRAMEWORK_LABEL[state.reportingFramework]} · ${basisForFramework(state.reportingFramework)} basis`],
    ['Tax', TAX_LABEL[state.taxRegime]],
    ['Industry', INDUSTRY_LABEL[state.industryTemplate]],
    ...(inventoryRelevant(state) ? [['Inventory costing', INVENTORY_LABEL[effectiveInventoryMethod(state)]] as [string, string]] : []),
  ];
  return (
    <div>
      <dl className="rounded-xl bg-[var(--bg-subtle)] divide-y divide-[var(--bg-surface)]">
        {rows.map(([label, value]) => (
          <div key={label} className="flex items-start justify-between gap-4 px-3.5 py-2.5 text-sm">
            <dt className="text-[var(--text-muted)]">{label}</dt>
            <dd className="text-[var(--text-main)] font-medium text-right break-words">{value}</dd>
          </div>
        ))}
      </dl>
      <p className="text-[11px] text-[var(--text-muted)] mt-3">
        We will set up your chart of accounts and defaults. No sample transactions are added.
      </p>
    </div>
  );
};
