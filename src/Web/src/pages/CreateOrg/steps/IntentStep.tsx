import React from 'react';
import type { Plan } from '../lib/policyDefaults';
import { ChoiceCards } from './ui';

export const IntentStep: React.FC<{ plan: Plan; onChange: (plan: Plan) => void }> = ({ plan, onChange }) => (
  <ChoiceCards<Plan>
    legend="What do you need?"
    name="plan"
    value={plan}
    onChange={onChange}
    columns="sm:grid-cols-3"
    options={[
      { value: 'invoicing', title: 'Invoicing', description: 'Send invoices and track what clients owe you.' },
      { value: 'accounting', title: 'Full accounting', description: 'Double-entry books, bills, reports and the general ledger.', badge: 'Most popular' },
      { value: 'enterprise', title: 'Enterprise', description: 'Everything in accounting plus inventory and audit tools.' },
    ]}
  />
);
