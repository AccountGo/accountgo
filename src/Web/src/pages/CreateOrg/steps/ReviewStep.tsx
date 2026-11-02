import React from 'react';
import { fiscalYearPreview } from '../lib/fyPreview';
import { basisForFramework, effectiveInventoryMethod, inventoryRelevant, type WizardState } from '../lib/policyDefaults';

const PLAN_LABEL = { invoicing: 'Invoicing', accounting: 'Full accounting', enterprise: 'Enterprise' } as const;
const FRAMEWORK_LABEL = { IfrsForSmes: 'IFRS for SMEs', UsGaapPrivate: 'US GAAP (private)', Ifrs: 'Full IFRS', CashBasis: 'Cash basis' } as const;
const TAX_LABEL = { None: 'No tax', SalesTax: 'Sales tax', VatGst: 'VAT / GST' } as const;
const INDUSTRY_LABEL = { Services: 'Services', ProductRetail: 'Products / retail', Mixed: 'Mixed' } as const;
const INVENTORY_LABEL = { None: 'None', WeightedAverage: 'Weighted average', Fifo: 'FIFO' } as const;
const PURPOSE_LABEL = { Personal: 'Personal finances', Business: 'Business or freelance work' } as const;
const COLLABORATION_LABEL = { JustMe: 'Just me', WithOthers: 'Me and others' } as const;

export const ReviewStep: React.FC<{ state: WizardState; capabilities?: { soloSetupEnabled: boolean } | null }> = ({ state, capabilities }) => {
  const preview = fiscalYearPreview(state.booksStartDate, state.fiscalYearPattern, state.fiscalYearStartMonth);
  const justMe = state.collaborationIntent === 'JustMe';
  const soloSetupAvailable = capabilities?.soloSetupEnabled !== false;
  const rows: [string, string][] = [
    ...(soloSetupAvailable && state.workspacePurpose ? [['Purpose', PURPOSE_LABEL[state.workspacePurpose]] as [string, string]] : []),
    ...(soloSetupAvailable && state.collaborationIntent ? [['Who uses it', COLLABORATION_LABEL[state.collaborationIntent]] as [string, string]] : []),
    [
      'Your access',
      soloSetupAvailable
        ? (justMe ? 'Owner · Full workspace access' : 'Owner · Full workspace access (you can narrow later)')
        : 'Owner · Standard access setup required',
    ],
    ...(soloSetupAvailable && justMe
      ? [[
          'Journal posting',
          'You can prepare and post your own journals under the single-user workflow.',
        ] as [string, string]]
      : !soloSetupAvailable
        ? [[
            'Journal posting',
            'Standard accounting controls apply until solo workflow is activated.',
          ] as [string, string]]
        : []),
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
      {!soloSetupAvailable && (
        <div role="note" className="p-3.5 mb-4 rounded-xl bg-[var(--bg-subtle)] text-[var(--text-secondary)] text-xs font-medium space-y-1">
          <p className="font-semibold text-[var(--text-main)]">Standard workspace setup</p>
          <p>
            Workspace purpose and collaboration choices are not saved and will not be applied to this workspace.
          </p>
        </div>
      )}
      <dl className="rounded-xl bg-[var(--bg-subtle)] divide-y divide-[var(--bg-surface)]">
        {rows.map(([label, value]) => (
          <div key={label} className="flex items-start justify-between gap-4 px-3.5 py-2.5 text-sm">
            <dt className="text-[var(--text-muted)]">{label}</dt>
            <dd className="text-[var(--text-main)] font-medium text-right break-words">{value}</dd>
          </div>
        ))}
      </dl>
      {justMe && soloSetupAvailable && (
        <p className="text-[11px] text-[var(--text-muted)] mt-3">
          You’ll have full access to the tools included in your plan. You can invite someone later.
        </p>
      )}
      {!soloSetupAvailable && (
        <p className="text-[11px] text-[var(--text-muted)] mt-3">
          Standard access setup is required after creation. You can assign functional roles and configure permissions in Members settings.
        </p>
      )}
      <p className="text-[11px] text-[var(--text-muted)] mt-3">
        We will set up your chart of accounts and defaults. No sample transactions are added.
      </p>
    </div>
  );
};
