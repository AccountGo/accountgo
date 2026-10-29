import React from 'react';
import type { StepErrors, WizardState } from '../lib/policyDefaults';
import { Field, inputClass } from './ui';

export const IdentityStep: React.FC<{
  state: WizardState;
  errors: StepErrors;
  update: (patch: Partial<WizardState>) => void;
}> = ({ state, errors, update }) => (
  <div className="space-y-4">
    <Field id="org-name" label="Workspace name" error={errors.name} hint="Your trading name, e.g. Pacific Rim Trading.">
      <input
        id="org-name"
        type="text"
        autoComplete="organization"
        value={state.name}
        onChange={(event) => update({ name: event.target.value })}
        aria-invalid={Boolean(errors.name)}
        aria-describedby={errors.name ? 'org-name-error' : 'org-name-hint'}
        className={inputClass}
      />
    </Field>
    <Field id="org-slug" label="Workspace URL" error={errors.slug} hint="Used in links to your workspace.">
      <div className="flex items-center rounded-xl bg-[var(--bg-subtle)] shadow-inner px-3.5 h-11 text-sm focus-within:ring-2 focus-within:ring-[var(--brand-ring)]">
        <span className="text-[var(--text-muted)] font-mono text-xs">/org/</span>
        <input
          id="org-slug"
          type="text"
          value={state.slug}
          onChange={(event) => update({ slug: event.target.value })}
          aria-invalid={Boolean(errors.slug)}
          aria-describedby={errors.slug ? 'org-slug-error' : 'org-slug-hint'}
          className="flex-1 bg-transparent text-[var(--text-main)] font-mono text-xs focus:outline-none ml-1"
        />
      </div>
    </Field>
    <Field id="org-legal-name" label="Legal name (optional)" hint="Shown on invoices when it differs from the workspace name.">
      <input
        id="org-legal-name"
        type="text"
        value={state.legalName}
        onChange={(event) => update({ legalName: event.target.value })}
        className={inputClass}
      />
    </Field>
  </div>
);
