// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import React, { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import {
  describePartyMatchReason,
  PartyMatchCandidate,
} from './fields';

export interface PartyMatchPromptProps {
  role: 'vendor' | 'client';
  extractedName: string | null;
  candidates: readonly PartyMatchCandidate[];
  autoMatched: PartyMatchCandidate | null;
  canManage: boolean;
  selectedPartyId: string;
  orgSlug: string;
  onSelectCandidate: (candidate: PartyMatchCandidate) => void;
  onCreateNew: (initialName: string) => void;
  onDismiss: () => void;
}

export const PartyMatchPrompt: React.FC<PartyMatchPromptProps> = ({
  role,
  extractedName,
  candidates,
  autoMatched,
  canManage,
  selectedPartyId,
  orgSlug,
  onSelectCandidate,
  onCreateNew,
  onDismiss,
}) => {
  const [rejectedCandidates, setRejectedCandidates] = useState(false);

  useEffect(() => {
    setRejectedCandidates(false);
  }, [extractedName, candidates, autoMatched]);

  if (autoMatched) {
    return (
      <div
        role="status"
        className="mb-4 rounded-xl border border-[var(--brand-ring)]/40 bg-[var(--brand-subtle)]/40 p-3.5 flex flex-wrap items-center justify-between gap-2 text-xs"
      >
        <span>
          Matched {role} from scan:{' '}
          <strong className="text-[var(--text-main)]">{autoMatched.name}</strong>{' '}
          <span className="text-[var(--text-secondary)]">
            ({describePartyMatchReason(autoMatched.reason)})
          </span>
        </span>
        <button
          type="button"
          onClick={onDismiss}
          className="px-2.5 py-1.5 font-semibold rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-surface)] text-[var(--text-secondary)]"
        >
          Dismiss
        </button>
      </div>
    );
  }

  if (candidates.length > 0 && !rejectedCandidates) {
    const detailBase =
      role === 'vendor'
        ? `/org/${orgSlug}/purchases/vendors`
        : `/org/${orgSlug}/sales/clients`;

    return (
      <div
        role="status"
        className="mb-4 rounded-xl border border-[var(--brand-ring)]/40 bg-[var(--brand-subtle)]/40 p-3.5 space-y-2.5 text-xs"
      >
        <div className="flex flex-wrap items-center justify-between gap-2">
          <span className="font-semibold text-[var(--text-main)]">
            Did you mean one of these existing {role}s?
            {extractedName ? (
              <span className="font-normal text-[var(--text-secondary)]">
                {' '}
                (scanned: &ldquo;{extractedName}&rdquo;)
              </span>
            ) : null}
          </span>
          <div className="flex items-center gap-2">
            {canManage && extractedName && !selectedPartyId && (
              <button
                type="button"
                onClick={() => setRejectedCandidates(true)}
                className="px-2.5 py-1 font-semibold rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-surface)] text-[var(--text-secondary)] hover:text-[var(--text-main)]"
              >
                None of these
              </button>
            )}
            <button
              type="button"
              onClick={onDismiss}
              className="px-2.5 py-1 font-semibold rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-surface)] text-[var(--text-secondary)]"
            >
              Dismiss
            </button>
          </div>
        </div>

        <ul className="space-y-1.5">
          {candidates.slice(0, 3).map((candidate) => (
            <li
              key={candidate.partyId}
              className="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-surface)] px-3 py-2"
            >
              <div className="flex flex-wrap items-center gap-2">
                <strong className="text-[var(--text-main)]">{candidate.name}</strong>
                <span className="px-2 py-0.5 rounded-full bg-[var(--bg-muted)] text-[var(--text-secondary)]">
                  {describePartyMatchReason(candidate.reason)}
                </span>
                {candidate.archived && (
                  <span className="px-2 py-0.5 rounded-full bg-amber-500/15 text-amber-700 dark:text-amber-300 font-medium">
                    archived
                  </span>
                )}
              </div>
              {candidate.archived ? (
                <Link
                  to={`${detailBase}/${candidate.partyId}`}
                  className="text-[var(--brand-primary)] font-semibold hover:underline"
                >
                  Unarchive {role} to use
                </Link>
              ) : (
                <button
                  type="button"
                  onClick={() => onSelectCandidate(candidate)}
                  className="px-3 py-1 font-semibold rounded-lg bg-[var(--brand-primary)] text-white hover:opacity-90 transition"
                >
                  Use {role}
                </button>
              )}
            </li>
          ))}
        </ul>
      </div>
    );
  }

  if (extractedName && !selectedPartyId && canManage && (candidates.length === 0 || rejectedCandidates)) {
    return (
      <div
        role="status"
        className="mb-4 rounded-xl border border-[var(--brand-ring)]/40 bg-[var(--brand-subtle)]/40 p-3.5 flex flex-wrap items-center justify-between gap-2 text-xs"
      >
        <span>
          No existing {role} matches{' '}
          <strong className="text-[var(--text-main)]">{extractedName}</strong>.
        </span>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={() => onCreateNew(extractedName)}
            className="px-3 py-1.5 font-semibold rounded-lg bg-[var(--brand-primary)] text-white hover:opacity-90 transition"
          >
            Create {role} &ldquo;{extractedName}&rdquo;
          </button>
          <button
            type="button"
            onClick={onDismiss}
            className="px-2.5 py-1.5 font-semibold rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-surface)] text-[var(--text-secondary)]"
          >
            Dismiss
          </button>
        </div>
      </div>
    );
  }

  return null;
};
