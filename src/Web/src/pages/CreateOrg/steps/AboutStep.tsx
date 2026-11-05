import React from 'react';
import type { CollaborationIntent, StepErrors, WizardState, WorkKind, LegalForm } from '../lib/policyDefaults';
import { ChoiceCards, type ChoiceOption } from './ui';

export interface SetupCapabilities {
  soloSetupEnabled: boolean;
  personalPurposeEnabled: boolean;
  controlledRecoveryEnabled: boolean;
  canAddPersonalBooks?: boolean;
}

export const AboutStep: React.FC<{
  state: WizardState;
  errors: StepErrors;
  update: (patch: Partial<WizardState>) => void;
  capabilities?: SetupCapabilities | null;
  onPersonalOnly?: () => void;
}> = ({ state, errors, update, capabilities, onPersonalOnly }) => {
  const soloSetupAvailable = capabilities?.soloSetupEnabled !== false;
  const purposeOptions: ChoiceOption<WorkKind>[] = [
    { value: 'Freelance', title: 'Freelance or self-employed',
      description: 'Your own work — clients, invoices and expenses. Personal money can go in linked personal books.' },
    { value: 'Company', title: 'A business or company',
      description: 'A separate legal business, such as a company, partnership, cooperative or non-profit.' },
  ];
  const legalOptions: ChoiceOption<LegalForm>[] = [
    { value: 'Corporation', title: 'Company', description: '' },
    { value: 'Partnership', title: 'Partnership', description: '' },
    { value: 'Cooperative', title: 'Cooperative', description: '' },
    { value: 'NonProfit', title: 'Non-profit', description: '' },
  ];

  const collaborationOptions: ChoiceOption<CollaborationIntent>[] = [
    {
      value: 'JustMe',
      title: 'Just me',
      description: soloSetupAvailable
        ? 'I’ll manage this workspace myself.'
        : 'Manage this workspace yourself under standard access controls.',
    },
    {
      value: 'WithOthers',
      title: 'Me and others',
      description: soloSetupAvailable
        ? 'I’ll share access with colleagues, partners, or an accountant.'
        : 'Share access with colleagues, partners, or an accountant under standard controls.',
    },
  ];

  const personalOnlyLink = capabilities?.canAddPersonalBooks === true && (
    <button type="button" onClick={onPersonalOnly}
      className="text-xs text-[var(--brand-primary)] underline cursor-pointer">Only want to track personal money?</button>
  );

  if (!soloSetupAvailable) {
    return (
      <div className="space-y-4">
        <div role="note" className="p-4 rounded-xl bg-[var(--bg-subtle)] text-[var(--text-secondary)] text-xs space-y-2">
          <p className="font-semibold text-[var(--text-main)]">Standard workspace setup</p>
          <p>
            Solo workspace auto-initialization is disabled; workspaces are created under standard access controls.
          </p>
          <p className="text-[var(--text-muted)]">
            Workspace purpose and collaboration choices cannot be applied and will not be saved. You can assign functional roles and manage permissions in Members settings after creation.
          </p>
        </div>
        {personalOnlyLink}
      </div>
    );
  }

  return (
    <div className="space-y-5">
      <div>
        <ChoiceCards<WorkKind>
          legend="What is this workspace for?"
          name="workKind"
          value={state.workKind}
          onChange={(workKind) => update({ workKind, workspacePurpose: 'Business' })}
          options={purposeOptions}
        />
        {errors.workKind && <p role="alert" className="text-[11px] text-rose-400 mt-1.5">{errors.workKind}</p>}
      </div>
      {state.workKind === 'Company' && <div>
        <ChoiceCards<LegalForm> legend="What kind of business?" name="legalForm" value={state.legalForm}
          onChange={(legalForm) => update({ legalForm })} options={legalOptions} />
        {errors.legalForm && <p role="alert" className="text-[11px] text-rose-400 mt-1.5">{errors.legalForm}</p>}
      </div>}
      {state.workKind && capabilities?.canAddPersonalBooks === true && <div>
        <ChoiceCards<'yes' | 'no'> legend="Also keep your personal money?" name="addPersonalBooks"
          value={state.addPersonalBooks === null ? '' : state.addPersonalBooks ? 'yes' : 'no'}
          onChange={(value) => update({ addPersonalBooks: value === 'yes' })}
          options={[
            { value: 'yes', title: 'Yes, set up my free personal books too', description: '' },
            { value: 'no', title: 'Not now', description: '' },
          ]} />
        {errors.addPersonalBooks && <p role="alert" className="text-[11px] text-rose-400 mt-1.5">{errors.addPersonalBooks}</p>}
        <p className="text-[11px] text-[var(--text-muted)] mt-2">Personal books are always free and don't count toward your free workspace. You can add them later from the workspace switcher.</p>
      </div>}
      {personalOnlyLink}

      <div>
        <ChoiceCards<CollaborationIntent>
          legend="Who will use this workspace?"
          name="collaborationIntent"
          value={state.collaborationIntent}
          onChange={(collaborationIntent) => update({ collaborationIntent })}
          options={collaborationOptions}
        />
        {errors.collaborationIntent && (
          <p role="alert" className="text-[11px] text-rose-400 mt-1.5">{errors.collaborationIntent}</p>
        )}
        {state.collaborationIntent === 'WithOthers' && (
          <p className="text-[11px] text-[var(--text-muted)] mt-2">
            You’ll keep full access as the owner. Journals will need independent review: another Owner or Admin who can post journals approves each one before it is posted.
          </p>
        )}
        {state.collaborationIntent === 'JustMe' && (
          <p className="text-[11px] text-[var(--text-muted)] mt-2">
            You’ll have full access to the tools included in your plan. You can invite someone later.
          </p>
        )}
      </div>
    </div>
  );
};
