import { describe, expect, it } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import { ReviewStep } from '../src/pages/CreateOrg/steps/ReviewStep';
import { AboutStep } from '../src/pages/CreateOrg/steps/AboutStep';
import { buildPayload, defaultState, normalizeWizardState, validateStep, type WorkKind, type LegalForm } from '../src/pages/CreateOrg/lib/policyDefaults';

describe('work workspace choices', () => {
  it('reviews the company legal form and linked personal books', () => {
    render(<ReviewStep state={{ ...defaultState(), workKind: 'Company', legalForm: 'Partnership', workspacePurpose: 'Business', addPersonalBooks: true }} capabilities={{ soloSetupEnabled: true, canAddPersonalBooks: true }} />);
    expect(screen.getByText('A business or company · Partnership')).toBeTruthy();
    expect(screen.getByText('Free, linked to this workspace')).toBeTruthy();
    cleanup();
  });

  it('offers work choices and requires personal consent only when available', () => {
    const capabilities = { soloSetupEnabled: true, personalPurposeEnabled: true, controlledRecoveryEnabled: false, canAddPersonalBooks: true };
    const state = { ...defaultState(), workKind: 'Freelance' as const, workspacePurpose: 'Business' as const, collaborationIntent: 'JustMe' as const };
    render(<AboutStep state={state} errors={{}} update={() => {}} capabilities={capabilities} />);
    expect(screen.getByRole('radio', { name: /Freelance or self-employed/ })).toBeTruthy();
    expect(screen.getByRole('radio', { name: /A business or company/ })).toBeTruthy();
    expect(screen.queryByText('Personal finances')).toBeNull();
    expect(screen.getByRole('radio', { name: /Not now/ })).toBeTruthy();
    expect(validateStep('about', state, new Date(), capabilities)).toHaveProperty('addPersonalBooks');
    cleanup();
    render(<AboutStep state={state} errors={{}} update={() => {}} capabilities={{ ...capabilities, canAddPersonalBooks: false }} />);
    expect(screen.queryByRole('radio', { name: /Not now/ })).toBeNull();
    expect(validateStep('about', state, new Date(), { ...capabilities, canAddPersonalBooks: false })).toEqual({});
    cleanup();
  });
  it('derives Business purpose from a work choice even before state normalization', () => {
    expect(buildPayload({ ...defaultState(), workKind: 'Freelance' }).workspacePurpose).toBe('Business');
  });
  it('keeps personal-only setup available under standard workspace controls', () => {
    render(<AboutStep state={defaultState()} errors={{}} update={() => {}} capabilities={{ soloSetupEnabled: false, personalPurposeEnabled: false, controlledRecoveryEnabled: false, canAddPersonalBooks: true }} />);
    expect(screen.getByRole('button', { name: 'Only want to track personal money?' })).toBeTruthy();
    cleanup();
  });
  it('requires an explicit work kind and company legal form', () => {
    const state = { ...defaultState(), workspacePurpose: 'Business' as const, collaborationIntent: 'JustMe' as const, workKind: '' as WorkKind, legalForm: '' as LegalForm, addPersonalBooks: null };
    expect(validateStep('about', state)).toHaveProperty('workKind');
    expect(validateStep('about', { ...state, workKind: 'Company' })).toHaveProperty('legalForm');
  });

  it.each([
    ['Freelance', '', 'SoleProprietor'],
    ['Company', 'Partnership', 'Partnership'],
  ] as const)('sends the selected legal form for %s', (workKind, legalForm, entityType) => {
    const payload = buildPayload({ ...defaultState(), workspacePurpose: 'Business', workKind, legalForm, addPersonalBooks: false });
    expect(payload).toMatchObject({ workspacePurpose: 'Business', entityType });
  });

  it('resets old Personal and Business draft choices without inventing answers', () => {
    for (const workspacePurpose of ['Personal', 'Business'] as const) {
      expect(normalizeWizardState({ workspacePurpose })).toMatchObject({ workspacePurpose: '', workKind: '' as WorkKind, legalForm: '' as LegalForm, addPersonalBooks: null });
    }
  });
});
