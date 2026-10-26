import React from 'react';
import type { Plan } from '../lib/policyDefaults';
import { usePlanCatalog } from '../../../lib/usePlanCatalog';
import { ChoiceCards, type ChoiceOption } from './ui';

const OPTIONS: ChoiceOption<Plan>[] = [
  { value: 'invoicing', title: 'Invoicing', description: 'Send invoices and track what clients owe you.' },
  { value: 'accounting', title: 'Full accounting', description: 'Double-entry books, bills, reports and the general ledger.', badge: 'Most popular' },
  { value: 'enterprise', title: 'Enterprise', description: 'Everything in accounting plus inventory and audit tools.' },
];

export const IntentStep: React.FC<{ plan: Plan; onChange: (plan: Plan) => void }> = ({ plan, onChange }) => {
  // Plans the operator assigns (e.g. Enterprise on hosted) are not offered here; the server enforces the same rule.
  const { plans } = usePlanCatalog();
  const options = plans
    ? OPTIONS.filter((option) => plans.find((p) => p.key === option.value)?.selfService !== false)
    : OPTIONS;
  return (
    <ChoiceCards<Plan>
      legend="What do you need?"
      name="plan"
      value={plan}
      onChange={onChange}
      columns={options.length === 3 ? 'sm:grid-cols-3' : 'sm:grid-cols-2'}
      options={options}
    />
  );
};
