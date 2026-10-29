/** Pure helpers for the reconciliation workspace (keyboard focus, search, tick batches). */

export interface ReconcileLineRef {
  lineId: number;
  in: number;
  out: number;
  ticked: boolean;
  type: string;
  reference?: string | null;
  payee?: string | null;
}

/** Move focus within a flattened list; wraps at the ends. */
export function nextFocusIndex(length: number, current: number, delta: number): number {
  if (length <= 0) return 0;
  const start = Number.isFinite(current) ? current : 0;
  return (start + delta + length * 1000) % length;
}

/** Friendly labels for DocumentTypes enum names (and opening-balance strings). */
export function reconcileLineTypeLabel(type: string): string {
  if (type.startsWith('Opening balance')) return type;
  switch (type) {
    case 'SalesReceipt':
      return 'Receipt';
    case 'PurchaseInvoicePayment':
      return 'Bill payment';
    case 'JournalEntry':
      return 'Journal';
    case 'SalesInvoice':
      return 'Invoice';
    case 'PurchaseInvoice':
      return 'Bill';
    case 'BankTransaction':
      return 'Bank transaction';
    case 'SalesCreditMemo':
      return 'Credit memo';
    case 'SalesDebitMemo':
      return 'Debit memo';
    case 'PurchaseCreditMemo':
      return 'Bill credit';
    case 'PurchaseDebitMemo':
      return 'Bill debit';
    case 'PurchaseReceipt':
      return 'Purchase receipt';
    default:
      return type.replace(/([a-z])([A-Z])/g, '$1 $2');
  }
}

/** Case-insensitive match on amount (exact to 2dp), type, reference, or payee. */
export function lineMatchesSearch(line: ReconcileLineRef, rawQuery: string): boolean {
  const query = rawQuery.trim().toLowerCase();
  if (!query) return true;

  const amount = line.in || line.out;
  const amountText = amount.toFixed(2);
  if (amountText.includes(query) || String(amount).includes(query)) return true;

  const haystack = [
    line.type,
    reconcileLineTypeLabel(line.type),
    line.reference ?? '',
    line.payee ?? '',
  ]
    .join(' ')
    .toLowerCase();
  return haystack.includes(query);
}

export function filterVisibleLines(
  lines: ReconcileLineRef[],
  search: string,
  hideTicked: boolean,
): ReconcileLineRef[] {
  return lines.filter((line) => {
    if (hideTicked && line.ticked) return false;
    return lineMatchesSearch(line, search);
  });
}

/** Build add/remove arrays for PUT Items from the previous ticked set and a toggled line. */
export function tickTogglePayload(
  previouslyTicked: ReadonlySet<number>,
  lineId: number,
): { add: number[]; remove: number[] } {
  if (previouslyTicked.has(lineId)) {
    return { add: [], remove: [lineId] };
  }
  return { add: [lineId], remove: [] };
}

/** Server difference must be exactly 0.00 before Finish is enabled. */
export function canFinishReconciliation(difference: number): boolean {
  return Math.round((difference + Number.EPSILON) * 100) === 0;
}

export const RECONCILE_TICK_DEBOUNCE_MS = 400;

export type TickBatch = { add: number[]; remove: number[] };

export type PendingTickSets = {
  add: Set<number>;
  remove: Set<number>;
};

export function createEmptyPendingTicks(): PendingTickSets {
  return { add: new Set(), remove: new Set() };
}

export function hasPendingTicks(pending: PendingTickSets): boolean {
  return pending.add.size > 0 || pending.remove.size > 0;
}

export function mergeTickIntoPending(
  pending: PendingTickSets,
  previouslyTicked: ReadonlySet<number>,
  lineId: number,
): void {
  const { add, remove } = tickTogglePayload(previouslyTicked, lineId);
  mergeBatchIntoPending(pending, { add, remove });
}

export function mergeBatchIntoPending(pending: PendingTickSets, batch: TickBatch): void {
  for (const id of batch.add) {
    pending.remove.delete(id);
    pending.add.add(id);
  }
  for (const id of batch.remove) {
    pending.add.delete(id);
    pending.remove.add(id);
  }
}

export function takePendingTickBatch(pending: PendingTickSets): TickBatch {
  const batch = { add: [...pending.add], remove: [...pending.remove] };
  pending.add.clear();
  pending.remove.clear();
  return batch;
}

/** Overlay still-pending optimistic ticks onto a server line snapshot. */
export function applyPendingTicksToLines<T extends { lineId: number; ticked: boolean }>(
  lines: T[],
  pending: PendingTickSets,
): T[] {
  if (!hasPendingTicks(pending)) return lines;
  return lines.map((line) => {
    if (pending.add.has(line.lineId)) return { ...line, ticked: true };
    if (pending.remove.has(line.lineId)) return { ...line, ticked: false };
    return line;
  });
}

export function applyPendingTicksToRecon<
  T extends { deposits: L[]; payments: L[] },
  L extends { lineId: number; ticked: boolean },
>(recon: T, pending: PendingTickSets): T {
  if (!hasPendingTicks(pending)) return recon;
  return {
    ...recon,
    deposits: applyPendingTicksToLines(recon.deposits, pending),
    payments: applyPendingTicksToLines(recon.payments, pending),
  };
}

export type TickSavePutResult<T> =
  | { ok: true; recon: T }
  | { ok: false; conflict: true }
  | { ok: false; conflict: false; message: string };

/**
 * One-at-a-time tick save loop. Concurrent flush() calls share a single in-flight drain
 * and re-run if new pending ticks arrived afterward. On 409, reloads, re-applies pending,
 * and retries the put once.
 */
export class TickSaveController<T extends { id: number; version: number }> {
  readonly pending: PendingTickSets = createEmptyPendingTicks();
  private inFlight: Promise<T | null> | null = null;
  private readonly getRecon: () => T | null;
  private readonly isDraft: (recon: T) => boolean;
  private readonly putItems: (
    id: number,
    add: number[],
    remove: number[],
    version: number,
  ) => Promise<TickSavePutResult<T>>;
  private readonly reload: (id: number) => Promise<T>;
  private readonly onUpdate: (recon: T) => void;

  constructor(
    getRecon: () => T | null,
    isDraft: (recon: T) => boolean,
    putItems: (
      id: number,
      add: number[],
      remove: number[],
      version: number,
    ) => Promise<TickSavePutResult<T>>,
    reload: (id: number) => Promise<T>,
    onUpdate: (recon: T) => void,
  ) {
    this.getRecon = getRecon;
    this.isDraft = isDraft;
    this.putItems = putItems;
    this.reload = reload;
    this.onUpdate = onUpdate;
  }

  /** True when there are unsaved ticks or a save is already running. */
  get hasWork(): boolean {
    return this.inFlight != null || hasPendingTicks(this.pending);
  }

  flush(): Promise<T | null> {
    if (!this.inFlight) {
      this.inFlight = this.drain().finally(() => {
        this.inFlight = null;
      });
    }
    return this.inFlight.then((result) => {
      if (hasPendingTicks(this.pending)) {
        return this.flush();
      }
      return result ?? this.getRecon();
    });
  }

  private async drain(): Promise<T | null> {
    let latest = this.getRecon();
    if (!latest || !this.isDraft(latest)) return latest;

    while (hasPendingTicks(this.pending)) {
      const batch = takePendingTickBatch(this.pending);
      if (batch.add.length === 0 && batch.remove.length === 0) break;

      try {
        const server = await this.putWithConflictRetry(latest, batch);
        const overlay = applyPendingTicksToRecon(server, this.pending);
        this.onUpdate(overlay);
        latest = overlay;
      } catch (err) {
        try {
          const fresh = await this.reload(latest.id);
          this.onUpdate(applyPendingTicksToRecon(fresh, this.pending));
        } catch {
          /* keep prior UI */
        }
        throw err;
      }
    }

    return this.getRecon();
  }

  private async putWithConflictRetry(latest: T, batch: TickBatch): Promise<T> {
    const first = await this.putItems(latest.id, batch.add, batch.remove, latest.version);
    if (first.ok) return first.recon;

    if (!first.conflict) {
      mergeBatchIntoPending(this.pending, batch);
      throw new Error(first.message);
    }

    // 409 from our own outdated version: requeue, reload, re-apply, retry once.
    mergeBatchIntoPending(this.pending, batch);
    const fresh = await this.reload(latest.id);
    this.onUpdate(applyPendingTicksToRecon(fresh, this.pending));

    const retryBatch = takePendingTickBatch(this.pending);
    if (retryBatch.add.length === 0 && retryBatch.remove.length === 0) {
      return applyPendingTicksToRecon(fresh, this.pending);
    }

    const second = await this.putItems(fresh.id, retryBatch.add, retryBatch.remove, fresh.version);
    if (second.ok) return second.recon;

    mergeBatchIntoPending(this.pending, retryBatch);
    if (second.conflict) {
      throw new Error('Someone else changed this reconciliation.');
    }
    throw new Error(second.message);
  }
}
