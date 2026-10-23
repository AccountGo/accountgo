import React from 'react';
import { Package, Plus } from 'lucide-react';
import { ItemOption } from '../types';
import { PickerColumn, PickerDialog } from './PickerDialog';

/** Picks a catalog item for an invoice or bill line. */
export const CatalogPickerDialog: React.FC<{
  items: ItemOption[];
  /** The amount column's heading: the selling price on invoices, the cost on bills. */
  priceLabel?: string;
  selectedId?: number | null;
  formatPrice: (value: number) => string;
  onPick: (item: ItemOption) => void;
  /** Omitted when the user can't add items. */
  onAddNew?: () => void;
  onClose: () => void;
}> = ({ items, priceLabel = 'Price', selectedId, formatPrice, onPick, onAddNew, onClose }) => {
  const columns: PickerColumn<ItemOption>[] = [
    {
      header: 'Item',
      width: '1fr',
      render: (item) => <span className="block truncate text-[var(--text-main)]">{item.description}</span>,
    },
    {
      header: priceLabel,
      width: 'auto',
      align: 'right',
      render: (item) => (
        <span className="font-mono text-[var(--text-main)]">{item.price != null ? formatPrice(item.price) : '—'}</span>
      ),
    },
  ];
  // Codes are optional; only show the column when some item has one.
  if (items.some((i) => i.code)) {
    columns.unshift({
      header: 'Code',
      width: '5.5rem',
      render: (item) => <span className="block truncate font-mono text-xs text-[var(--text-muted)]">{item.code || '—'}</span>,
    });
  }

  return (
    <PickerDialog
      title="Pick from catalog"
      icon={Package}
      items={items}
      columns={columns}
      searchText={(item) => `${item.code ?? ''} ${item.description}`}
      searchPlaceholder="Search by name or code"
      selectedId={selectedId}
      selectedLabel="On this line"
      noun={['item', 'items']}
      footerAction={onAddNew && { label: 'New item', icon: Plus, onClick: onAddNew }}
      onPick={onPick}
      onClose={onClose}
    />
  );
};
