import React from 'react';
import type { CollaborationIntent, StepErrors, WizardState, WorkspacePurpose } from '../lib/policyDefaults';
import { ChoiceCards, type ChoiceOption } from './ui';

export interface SetupCapabilities {
  soloSetupEnabled: boolean;
  personalPurposeEnabled: boolean;
  controlledRecoveryEnabled: boolean;
}

export const AboutStep: React.FC<{
  state: WizardState;
  errors: StepErrors;
  update: (patch: Partial<WizardState>) => void;
  capabilities?: SetupCapabilities | null;
}> = ({ state, errors, update, capabilities }) => {
  const soloSetupAvailable = capabilities?.soloSetupEnabled !== false;
  const personalDisabled = capabilities?.personalPurposeEnabled === false;
  const purposeOptions: ChoiceOption<WorkspacePurpose>[] = [
    {
      value: 'Personal',
      title: 'Personal finances',
      description: personalDisabled
        ? 'Personal finance mapping is not yet approved.'
        : 'Manage your own money or household finances.',
      disabled: personalDisabled,
      badge: personalDisabled ? 'Coming soon' : undefined,
    },
    {
      value: 'Business',
      title: 'Business or freelance work',
      description: 'Manage income, expenses, invoices, and business records.',
    },
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
      </div>
    );
  }

  return (
    <div className="space-y-5">
      <div>
        <ChoiceCards<WorkspacePurpose>
          legend="What will you use this workspace for?"
          name="workspacePurpose"
          value={state.workspacePurpose}
          onChange={(workspacePurpose) => update({ workspacePurpose })}
          options={purposeOptions}
        />
        {errors.workspacePurpose && (
          <p role="alert" className="text-[11px] text-rose-400 mt-1.5">{errors.workspacePurpose}</p>
        )}
      </div>

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
