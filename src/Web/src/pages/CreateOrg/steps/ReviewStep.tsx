import React from 'react';
import type { PlanCatalogEntry } from '../../../lib/usePlanCatalog';
import { fiscalYearPreview } from '../lib/fyPreview';
import { basisForFramework, effectiveInventoryMethod, inventoryRelevant, type WizardState } from '../lib/policyDefaults';

const PLAN_LABEL: Record<string, string> = { invoicing: 'Invoicing', accounting: 'Full accounting', enterprise: 'Enterprise' };
const FRAMEWORK_LABEL = { IfrsForSmes: 'IFRS for SMEs', UsGaapPrivate: 'US GAAP (private)', Ifrs: 'Full IFRS', CashBasis: 'Cash basis' } as const;
const TAX_LABEL = { None: 'No tax', SalesTax: 'Sales tax', VatGst: 'VAT / GST' } as const;
const INDUSTRY_LABEL = { Services: 'Services', ProductRetail: 'Products / retail', Mixed: 'Mixed' } as const;
const INVENTORY_LABEL = { None: 'None', WeightedAverage: 'Weighted average', Fifo: 'FIFO' } as const;
const LEGAL_FORM_LABEL = { Corporation: 'Company', Partnership: 'Partnership', Cooperative: 'Cooperative', NonProfit: 'Non-profit' } as const;
const COLLABORATION_LABEL = { JustMe: 'Just me', WithOthers: 'Me and others' } as const;

export const ReviewStep: React.FC<{
  state: WizardState;
  capabilities?: { soloSetupEnabled: boolean; canAddPersonalBooks?: boolean } | null;
  isContinuation?: boolean;
  existingSettings?: { name?: string; slug?: string; plan?: string; currency?: string; fiscalYearEnd?: string } | null;
  catalog?: PlanCatalogEntry[] | null;
}> = ({ state, capabilities, isContinuation, existingSettings, catalog }) => {
  const planLabel = PLAN_LABEL[state.plan]
    || catalog?.find((p) => p.key === state.plan)?.name
    || state.plan;
  const preview = fiscalYearPreview(state.booksStartDate, state.fiscalYearPattern, state.fiscalYearStartMonth);
  const justMe = state.collaborationIntent === 'JustMe';
  const withOthers = state.collaborationIntent === 'WithOthers';
  const soloSetupAvailable = capabilities?.soloSetupEnabled !== false;

  const purposeLabel = state.workKind === 'Company'
    ? `A business or company · ${state.legalForm ? LEGAL_FORM_LABEL[state.legalForm] : '—'}`
    : state.workKind === 'Freelance' ? 'Freelance or self-employed' : 'Personal books';

  // About answers are the choices this setup applies; workspace facts below remain read-only on continuation.
  const aboutRows: [string, string][] = [
    ...(soloSetupAvailable && state.workspacePurpose ? [['Purpose', purposeLabel] as [string, string]] : []),
    ...(soloSetupAvailable && state.workKind && capabilities?.canAddPersonalBooks === true
      ? [['Personal books', state.addPersonalBooks ? 'Free, linked to this workspace' : 'Not now'] as [string, string]] : []),
    ...(soloSetupAvailable && state.collaborationIntent ? [['Who uses it', COLLABORATION_LABEL[state.collaborationIntent]] as [string, string]] : []),
    [
      'Your access',
      soloSetupAvailable
        ? (justMe ? 'Owner · Full workspace access' : 'Owner · Full workspace access (you can narrow later)')
        : 'Owner · Standard access setup required',
    ],
    ...(soloSetupAvailable && justMe
      ? [['Journal posting', 'You can prepare and post your own journals under the single-user workflow.'] as [string, string]]
      : soloSetupAvailable && withOthers
        ? [['Journal posting', 'Independent review: another Owner or Admin who can post journals approves each journal before it is posted.'] as [string, string]]
      : !soloSetupAvailable
        ? [['Journal posting', 'Standard accounting controls apply until solo workflow is activated.'] as [string, string]]
        : []),
  ];

  // In continuation mode, the workspace already exists: show its STORED facts (read-only), never the local
  // draft values, which this flow does not save. In create mode, show the values that WILL be saved.
  const workspaceRows: [string, string][] = isContinuation
    ? [
        ...(existingSettings?.plan ? [['Plan', existingSettings.plan] as [string, string]] : []),
        ...(existingSettings?.name ? [['Workspace', existingSettings.name] as [string, string]] : []),
        ...(existingSettings?.slug ? [['URL', `/org/${existingSettings.slug}`] as [string, string]] : []),
        ...(existingSettings?.currency ? [['Currency', existingSettings.currency.toUpperCase()] as [string, string]] : []),
        ...(existingSettings?.fiscalYearEnd ? [['Fiscal year end', existingSettings.fiscalYearEnd] as [string, string]] : []),
      ]
    : [
        ['Plan', planLabel],
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

  const rows: [string, string][] = [...aboutRows, ...workspaceRows];
  return (
    <div>
      {isContinuation && (
        <div role="note" className="p-3.5 mb-4 rounded-xl bg-[var(--bg-subtle)] text-[var(--text-secondary)] text-xs font-medium space-y-1">
          <p className="font-semibold text-[var(--text-main)]">Finishing setup for your workspace</p>
          <p>
            The details below were set when this workspace was created and are shown for reference — finishing setup does not change them. Finishing setup applies your business type, access and journal workflow. You can change your plan or other settings anytime afterward.
          </p>
        </div>
      )}
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
      {state.workKind === 'Freelance' && soloSetupAvailable && <p className="text-[11px] text-[var(--text-muted)] mt-3">
        Freelance books keep money you take for personal use separate from your work profit.
      </p>}
      {justMe && soloSetupAvailable && (
        <p className="text-[11px] text-[var(--text-muted)] mt-3">
          You’ll have full access to the tools included in your plan. You can invite someone later.
        </p>
      )}
      {withOthers && soloSetupAvailable && (
        <p className="text-[11px] text-[var(--text-muted)] mt-3">
          You keep full access as the owner. Journals need a second person to approve them before posting, so invite an accountant or partner
          from Team &amp; Members once the workspace is ready. If you prefer to post your own journals, you can change the journal approval
          policy yourself later in Settings.
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
