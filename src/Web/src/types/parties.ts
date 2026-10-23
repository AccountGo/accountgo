/**
 * What clients and vendors share: both are parties, the same details and balances seen from the side of sales or
 * purchases. What each one owes or is owed comes from its issued invoices or approved bills.
 */
export interface PartyBase {
  id: number;
  name: string;
  email?: string | null;
  phone?: string | null;
  street?: string | null;
  /** City and state or province, one field as documents print it. */
  city?: string | null;
  postalCode?: string | null;
  country?: string | null;
  /** Null: new documents use the workspace's default terms. */
  paymentTermId?: number | null;
  archived: boolean;
  /** Nothing recorded for it yet, so it can be deleted rather than archived. */
  canDelete?: boolean;
  /** Unpaid on its issued invoices or approved bills. */
  outstanding: number;
  /** The part of it past due. */
  overdue: number;
  /** Payments recorded against its documents. */
  paidToDate: number;
}
