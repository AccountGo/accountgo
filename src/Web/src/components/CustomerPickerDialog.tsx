import React from 'react';
import { UserPlus, Users } from 'lucide-react';
import { CustomerOption } from '../types';
import { PickerDialog } from './PickerDialog';

/** Picks the client an invoice is billed to, with a shortcut to add a new one. */
export const CustomerPickerDialog: React.FC<{
  customers: CustomerOption[];
  selectedId?: number | null;
  onPick: (customer: CustomerOption) => void;
  /** Omitted when the user can't add clients. */
  onAddNew?: () => void;
  onClose: () => void;
}> = ({ customers, selectedId, onPick, onAddNew, onClose }) => (
  <PickerDialog
    title="Select a client"
    icon={Users}
    items={customers}
    columns={[
      {
        header: 'Client',
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
    noun={['client', 'clients']}
    footerAction={onAddNew && { label: 'New client', icon: UserPlus, onClick: onAddNew }}
    onPick={onPick}
    onClose={onClose}
  />
);
