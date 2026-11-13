import type { BankStatementLine, StatementOwnerAction, StatementOwnerActions } from '../types/banking';

type OwnerLine = Pick<BankStatementLine, 'amount' | 'postedDate' | 'isMatched' | 'isExcluded' | 'suggestedOwnerAction'>;

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
