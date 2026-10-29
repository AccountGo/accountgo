import React from 'react';
import { UserPlus, Users } from 'lucide-react';
import { CustomerOption } from '../types';
import { PickerDialog } from './PickerDialog';
import { PartyRole, partyRoles } from '../lib/partyRoles';

/** Picks the client an invoice is billed to, or the vendor a bill is from, with a shortcut to add a new one. */
export const PartyPickerDialog: React.FC<{
  role: PartyRole;
  parties: CustomerOption[];
  selectedId?: number | null;
  onPick: (party: CustomerOption) => void;
  /** Omitted when the user can't add clients or vendors. */
  onAddNew?: () => void;
  onClose: () => void;
}> = ({ role, parties, selectedId, onPick, onAddNew, onClose }) => {
  const config = partyRoles[role];
  return (
    <PickerDialog
      title={`Select a ${config.noun}`}
      icon={Users}
      items={parties}
      columns={[
        {
          header: config.Noun,
          width: '1fr',
          render: (c) => (
            <span className="block min-w-0">
              <span className="block truncate text-[var(--text-main)]">{c.name}</span>
              {c.email && <span className="block truncate text-xs text-[var(--text-muted)]">{c.email}</span>}
            </span>
          ),
        },
        {
          header: 'Phone',
          width: 'minmax(0, 9rem)',
          align: 'right',
          render: (c) => <span className="block truncate text-xs text-[var(--text-secondary)]">{c.phone || '—'}</span>,
        },
      ]}
      searchText={(c) => `${c.name} ${c.email ?? ''} ${c.phone ?? ''}`}
      searchPlaceholder="Search by name, email or phone"
      selectedId={selectedId}
      selectedLabel="Selected"
      noun={[config.noun, config.plural]}
      footerAction={onAddNew && { label: `New ${config.noun}`, icon: UserPlus, onClick: onAddNew }}
      onPick={onPick}
      onClose={onClose}
    />
  );
};
