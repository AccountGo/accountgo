// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { PartyMatchPrompt } from '../src/components/document-capture/PartyMatchPrompt';
import {
  PartyMatchCandidate,
  resolveAutoPreselectCandidate,
} from '../src/components/document-capture/fields';

afterEach(() => {
  cleanup();
});

describe('resolveAutoPreselectCandidate (M-05 / M-06)', () => {
  it('never pre-selects a name-only match even at score 1.0', () => {
    const candidates: PartyMatchCandidate[] = [
      { partyId: 'ven_1', name: 'Northwind Office Limited', reason: 'name', score: 1.0, archived: false },
    ];
    expect(resolveAutoPreselectCandidate(candidates)).toBeNull();
  });

  it('pre-selects a single non-archived alias or identifier match', () => {
    const aliasOnly: PartyMatchCandidate[] = [
      { partyId: 'ven_1', name: 'Northwind Office Limited', reason: 'alias', score: 1.0, archived: false },
    ];
    expect(resolveAutoPreselectCandidate(aliasOnly)?.partyId).toBe('ven_1');

    const identifierOnly: PartyMatchCandidate[] = [
      { partyId: 'cus_1', name: 'Blue Harbor Retail LLC', reason: 'identifier', score: 1.0, archived: false },
    ];
    expect(resolveAutoPreselectCandidate(identifierOnly)?.partyId).toBe('cus_1');
  });

  it('never pre-selects an archived party even on alias or identifier hit', () => {
    const archivedAlias: PartyMatchCandidate[] = [
      { partyId: 'ven_9', name: 'Legacy Supply Ltd', reason: 'alias', score: 1.0, archived: true },
    ];
    expect(resolveAutoPreselectCandidate(archivedAlias)).toBeNull();
  });

  it('never pre-selects when multiple strong candidates match', () => {
    const multipleStrong: PartyMatchCandidate[] = [
      { partyId: 'ven_1', name: 'Alpha Ltd', reason: 'identifier', score: 1.0, archived: false },
      { partyId: 'ven_2', name: 'Beta Ltd', reason: 'alias', score: 1.0, archived: false },
    ];
    expect(resolveAutoPreselectCandidate(multipleStrong)).toBeNull();
  });
});

describe('PartyMatchPrompt (M-05 / M-06)', () => {
  it('shows up to 3 candidates with reason labels and gates Create new behind None of these', () => {
    const onSelect = vi.fn();
    const onCreate = vi.fn();
    const candidates: PartyMatchCandidate[] = [
      { partyId: 'ven_1', name: 'Northwind Office Limited', reason: 'name', score: 0.96, archived: false },
      { partyId: 'ven_2', name: 'Northwind Supply Co', reason: 'name', score: 0.85, archived: false },
    ];

    render(
      <MemoryRouter>
        <PartyMatchPrompt
          role="vendor"
          extractedName="Northwind Office"
          candidates={candidates}
          autoMatched={null}
          canManage={true}
          selectedPartyId=""
          orgSlug="acme"
          onSelectCandidate={onSelect}
          onCreateNew={onCreate}
          onDismiss={vi.fn()}
        />
      </MemoryRouter>
    );

    expect(screen.getByText(/Did you mean one of these existing vendors/i)).toBeTruthy();
    expect(screen.getByText('Northwind Office Limited')).toBeTruthy();
    expect(screen.getAllByText('similar name')).toHaveLength(2);
    // Create vendor is hidden behind "None of these" while candidates exist
    expect(screen.queryByRole('button', { name: /Create vendor/i })).toBeNull();

    fireEvent.click(screen.getByRole('button', { name: /None of these/i }));
    const createBtn = screen.getByRole('button', { name: /Create vendor/i });
    fireEvent.click(createBtn);
    expect(onCreate).toHaveBeenCalledWith('Northwind Office');
  });

  it('shows Create new directly when there are no candidates', () => {
    const onCreate = vi.fn();
    render(
      <MemoryRouter>
        <PartyMatchPrompt
          role="client"
          extractedName="Cedar Pine LLC"
          candidates={[]}
          autoMatched={null}
          canManage={true}
          selectedPartyId=""
          orgSlug="acme"
          onSelectCandidate={vi.fn()}
          onCreateNew={onCreate}
          onDismiss={vi.fn()}
        />
      </MemoryRouter>
    );

    const createBtn = screen.getByRole('button', { name: /Create client/i });
    fireEvent.click(createBtn);
    expect(onCreate).toHaveBeenCalledWith('Cedar Pine LLC');
  });

  it('labels archived candidates with an unarchive link and no select button', () => {
    const candidates: PartyMatchCandidate[] = [
      { partyId: 'ven_arch', name: 'Old Harbor Supply', reason: 'alias', score: 1.0, archived: true },
    ];

    render(
      <MemoryRouter>
        <PartyMatchPrompt
          role="vendor"
          extractedName="Old Harbor"
          candidates={candidates}
          autoMatched={null}
          canManage={true}
          selectedPartyId=""
          orgSlug="acme"
          onSelectCandidate={vi.fn()}
          onCreateNew={vi.fn()}
          onDismiss={vi.fn()}
        />
      </MemoryRouter>
    );

    expect(screen.getByText('archived')).toBeTruthy();
    expect(screen.getByText('matched before')).toBeTruthy();
    const link = screen.getByRole('link', { name: /Unarchive vendor to use/i });
    expect(link.getAttribute('href')).toBe('/org/acme/purchases/vendors/ven_arch');
    expect(screen.queryByRole('button', { name: /Use vendor/i })).toBeNull();
  });
});
