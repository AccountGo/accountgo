import type { BankStatementLine, StatementOwnerAction, StatementOwnerActions } from '../types/banking';

type OwnerLine = Pick<BankStatementLine, 'amount' | 'postedDate' | 'isMatched' | 'isExcluded' | 'suggestedOwnerAction'>;

/** Prefer the linked Personal books workflow when the workspace and user can use it. */
export function ownerButtonSet({ linkedPersonalCompanyId, canCreateLinkedEntry, ownerActionsAvailable }: {
  linkedPersonalCompanyId?: number | null;
  canCreateLinkedEntry: boolean;
  ownerActionsAvailable: boolean;
}): 'linked' | 'owner' | 'none' {
  if (linkedPersonalCompanyId && canCreateLinkedEntry) return 'linked';
  return ownerActionsAvailable ? 'owner' : 'none';
}

/** A selection must contain eligible lines in one direction and the user must have both permissions. */
export function ownerActionForLines(
  lines: OwnerLine[], actions: StatementOwnerActions | null, canManage: boolean, canCreate: boolean,
  booksStart: string | null,
): StatementOwnerAction | null {
  if (!canManage || !canCreate || !actions || !lines.length) return null;
  if (lines.some(l => l.isMatched || l.isExcluded || (booksStart && String(l.postedDate).slice(0, 10) < booksStart))) return null;
  if (lines.every(l => l.amount < 0)) return actions.drawing;
  if (lines.every(l => l.amount > 0)) return actions.contribution;
  return null;
}

export function ownerSuggestionLabel(line: OwnerLine, action: StatementOwnerAction | null): string | null {
  const direction = line.amount < 0 ? 'drawing' : line.amount > 0 ? 'contribution' : null;
  return action && direction && line.suggestedOwnerAction === direction ? `Suggested: ${action.label}` : null;
}

/** Matched owner movements use a reversal so their original posting cannot be duplicated. */
export function matchedLineAction(
  line: Pick<BankStatementLine, 'isMatched' | 'isExcluded' | 'matchedOwnerAction'>,
  canManage: boolean, canCreate: boolean, buttons: 'owner' | 'linked' | 'none',
): 'undo' | 'unmatch' | null {
  if (!canManage || !line.isMatched || line.isExcluded) return null;
  return line.matchedOwnerAction && canCreate && buttons === 'owner' ? 'undo' : 'unmatch';
}
