import React from 'react';
import { CalendarDays } from 'lucide-react';
import { fiscalYearPreview } from '../lib/fyPreview';
import { CURRENCIES, type FiscalYearPattern, type StepErrors, type WizardState } from '../lib/policyDefaults';
import { ChoiceCards, Field, inputClass } from './ui';

const MONTH_NAMES = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];

export const MoneyTimeStep: React.FC<{
  state: WizardState;
  errors: StepErrors;
  update: (patch: Partial<WizardState>) => void;
}> = ({ state, errors, update }) => {
  const preview = fiscalYearPreview(state.booksStartDate, state.fiscalYearPattern, state.fiscalYearStartMonth);
  return (
    <div className="space-y-4">
      <Field id="org-currency" label="Currency" error={errors.currency} hint="This becomes your books currency. Amounts are never converted later.">
        <input
          id="org-currency"
          list="org-currency-options"
          value={state.currency}
          maxLength={3}
          autoCapitalize="characters"
          onChange={(event) => update({ currency: event.target.value.toUpperCase() })}
          aria-invalid={Boolean(errors.currency)}
          aria-describedby={errors.currency ? 'org-currency-error' : 'org-currency-hint'}
          className={`${inputClass} font-mono`}
        />
        <datalist id="org-currency-options">
          {CURRENCIES.map((code) => <option key={code} value={code} />)}
        </datalist>
      </Field>

      <Field id="org-books-start" label="Books start date" error={errors.booksStartDate} hint="The first day you will record transactions here. Opening balances can be added later.">
        <input
          id="org-books-start"
          type="date"
          value={state.booksStartDate}
          onChange={(event) => update({ booksStartDate: event.target.value })}
          aria-invalid={Boolean(errors.booksStartDate)}
          aria-describedby={errors.booksStartDate ? 'org-books-start-error' : 'org-books-start-hint'}
          className={inputClass}
        />
      </Field>

      <ChoiceCards<FiscalYearPattern>
        legend="Fiscal year"
        name="fiscalYearPattern"
        value={state.fiscalYearPattern}
        onChange={(fiscalYearPattern) => update({ fiscalYearPattern })}
        columns="sm:grid-cols-4"
        options={[
          { value: 'Calendar', title: 'Calendar', description: 'Jan – Dec' },
          { value: 'AprMar', title: 'Apr – Mar', description: 'Starts in April' },
          { value: 'JulJun', title: 'Jul – Jun', description: 'Starts in July' },
          { value: 'Custom', title: 'Custom', description: 'Pick a month' },
        ]}
      />

      {state.fiscalYearPattern === 'Custom' && (
        <Field id="org-fy-month" label="Fiscal year starts in" error={errors.fiscalYearStartMonth}>
          <select
            id="org-fy-month"
            value={state.fiscalYearStartMonth ?? ''}
            onChange={(event) => update({ fiscalYearStartMonth: event.target.value ? Number(event.target.value) : null })}
            aria-invalid={Boolean(errors.fiscalYearStartMonth)}
            className={`${inputClass} cursor-pointer`}
          >
            <option value="">Choose a month</option>
            {MONTH_NAMES.map((name, index) => <option key={name} value={index + 1}>{name}</option>)}
          </select>
        </Field>
      )}

      <div
        aria-live="polite"
        className="inline-flex items-center gap-2 px-3 py-2 rounded-xl bg-[var(--brand-subtle)] text-[var(--brand-primary)] text-xs font-semibold transition-all"
      >
        <CalendarDays className="w-4 h-4" aria-hidden="true" />
        <span>{preview ? preview.label : 'Enter a valid date to preview your first fiscal year'}</span>
      </div>
    </div>
  );
};
