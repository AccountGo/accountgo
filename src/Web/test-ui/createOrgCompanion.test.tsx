import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { CreateOrgPage } from '../src/pages/CreateOrg/CreateOrgPage';
import { defaultState } from '../src/pages/CreateOrg/lib/policyDefaults';

const { authFetch, navigate, refreshSession, params } = vi.hoisted(() => ({ authFetch: vi.fn(), navigate: vi.fn(), refreshSession: vi.fn(), params: new URLSearchParams() }));
vi.mock('../src/auth/AuthContext', () => ({ useAuth: () => ({ authFetch, refreshSession, user: { firstName: 'Pat', email: 'pat@example.com' } }) }));
vi.mock('react-router-dom', () => ({ useNavigate: () => navigate, useSearchParams: () => [params] }));
const json = (data: unknown, status = 200) => new Response(JSON.stringify(data), { status });
let personalFails = false;
let calls: string[];
let checkout = false;
let pending = false;
let soloEnabled = true;
beforeEach(() => {
  vi.clearAllMocks(); params.delete('continue'); personalFails = false; checkout = false; pending = false; soloEnabled = true; calls = [];
  localStorage.clear();
  localStorage.setItem('accountgo.createOrgDraft.v2', JSON.stringify({ stepIndex: 5, state: {
    ...defaultState(), name: 'Work', slug: 'work', currency: 'EUR', workspacePurpose: 'Business', collaborationIntent: 'JustMe', workKind: 'Freelance', addPersonalBooks: true,
  } }));
  authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (init?.method === 'POST' || init?.method === 'PUT') calls.push(url);
    if (url === '/api/organizations/setup-capabilities') return json({ soloSetupEnabled: soloEnabled, personalPurposeEnabled: true, controlledRecoveryEnabled: false, canAddPersonalBooks: true });
    if (url === '/api/organizations/current') return json({ id: '42', name: 'Work', slug: 'work', currency: 'EUR' });
    if (url === '/api/organizations/current/initialize') return json({ initialized: true });
    if (url === '/api/organizations/current/accounting-policy') return init?.method === 'PUT'
      ? json({ entityType: JSON.parse(String(init?.body)).entityType })
      : json({ entityType: 'Corporation', isLedgerFrozen: false });
    if (url === '/api/organizations/personal') return json({ slug: 'pat-personal' }, personalFails ? 500 : 201);
    if (url === '/api/organizations' && init?.method === 'POST') return json({ id: '42', slug: 'work', ...(checkout ? { checkoutUrl: 'https://checkout.example/pay' } : {}), ...(pending ? { activationState: 'pending_checkout' } : {}) }, 201);
    if (url === '/api/organizations') return json([]);
    return json({}, 404);
  });
});
afterEach(() => { cleanup(); vi.unstubAllGlobals(); localStorage.clear(); });
async function submit() {
  render(<CreateOrgPage />);
  await screen.findByRole('button', { name: 'Create workspace' });
  await waitFor(() => expect(authFetch).toHaveBeenCalledWith('/api/organizations/setup-capabilities'));
  fireEvent.click(screen.getByRole('button', { name: 'Create workspace' }));
}
describe('companion personal books', () => {
  it.each([false, true])('continues to checkout after the personal attempt (failure=%s)', async (fails) => {
    checkout = true;
    personalFails = fails;
    const assign = vi.fn(() => { calls.push('checkout'); });
    vi.stubGlobal('window', new Proxy(window, { get(target, key) {
      return key === 'location' ? { origin: 'http://localhost', assign } : Reflect.get(target, key);
    } }));
    await submit();
    await waitFor(() => expect(assign).toHaveBeenCalledWith('https://checkout.example/pay'));
    expect(calls).toEqual(['/api/organizations', '/api/organizations/personal', 'checkout']);
  });
  it('creates personal books before navigating to the pending workspace', async () => {
    pending = true;
    await submit();
    await waitFor(() => expect(navigate).toHaveBeenCalledWith('/org/work', { replace: true }));
    expect(calls).toEqual(['/api/organizations', '/api/organizations/personal']);
  });
  it('applies a company legal form through the existing policy endpoint after initialization', async () => {
    params.set('continue', 'work');
    const saved = JSON.parse(localStorage.getItem('accountgo.createOrgDraft.v2')!);
    saved.state.workKind = 'Company'; saved.state.legalForm = 'Partnership';
    localStorage.setItem('accountgo.createOrgDraft.v2', JSON.stringify(saved));
    const original = authFetch.getMockImplementation()!;
    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url === '/api/organizations/current/accounting-policy' && init?.method !== 'PUT') return json({ entityType: 'SoleProprietor', isLedgerFrozen: false });
      if (url === '/api/organizations/current/accounting-policy') {
        calls.push(url);
        expect(init?.headers).toMatchObject({ 'X-AccountGo-Org': 'work' });
        expect(JSON.parse(String(init?.body))).toEqual({ entityType: 'Partnership' });
        return json({ entityType: 'Partnership' });
      }
      return original(url, init);
    });
    await submit(); await screen.findByText('Your workspace is ready');
    expect(calls).toEqual(['/api/organizations/current/initialize', '/api/organizations/current/accounting-policy', '/api/organizations/personal']);
  });

  it('applies sole proprietor books for Freelance in continuation, so a retry after Company resets the legal form', async () => {
    params.set('continue', 'work');
    await submit(); await screen.findByText('Your workspace is ready');
    const policy = authFetch.mock.calls.find(([url, init]) => url === '/api/organizations/current/accounting-policy' && init?.method === 'PUT')!;
    expect(JSON.parse(String(policy[1].body))).toEqual({ entityType: 'SoleProprietor' });
    expect(calls).toEqual(['/api/organizations/current/initialize', '/api/organizations/current/accounting-policy', '/api/organizations/personal']);
  });

  it('does not change the books type when initialization rejects the workspace', async () => {
    params.set('continue', 'work');
    const original = authFetch.getMockImplementation()!;
    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url === '/api/organizations/current/initialize') { calls.push(url); return json({ reasonCode: 'not_pending_signup_workspace' }, 422); }
      return original(url, init);
    });
    await submit();
    await screen.findByRole('alert');
    expect(calls).toEqual(['/api/organizations/current/initialize']);
  });

  it('skips the change when the stored books type already matches', async () => {
    params.set('continue', 'work');
    const original = authFetch.getMockImplementation()!;
    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url === '/api/organizations/current/accounting-policy' && init?.method !== 'PUT') return json({ entityType: 'SoleProprietor', isLedgerFrozen: false });
      return original(url, init);
    });
    await submit(); await screen.findByText('Your workspace is ready');
    expect(calls).toEqual(['/api/organizations/current/initialize', '/api/organizations/personal']);
  });

  it('does not use a saved companion answer when standard setup hides the offer', async () => {
    soloEnabled = false;
    await submit(); await screen.findByText('Your workspace is ready');
    expect(calls).toEqual(['/api/organizations']);
  });
  it('creates linked personal books once before showing work success', async () => {
    await submit(); await screen.findByText('Your workspace is ready');
    const personal = authFetch.mock.calls.filter(([url]) => url === '/api/organizations/personal');
    expect(personal).toHaveLength(1);
    expect(JSON.parse(personal[0][1].body)).toEqual({ name: 'Pat — Personal', currency: 'EUR', linkWorkCompanyId: 42 });
    expect(calls).toEqual(['/api/organizations', '/api/organizations/personal']);
  });
  it('keeps work success and explains how to retry when personal creation fails', async () => {
    personalFails = true; await submit();
    expect(await screen.findByText("Your workspace is ready. Personal books weren't created — add them from the workspace switcher.")).toBeTruthy();
  });
  it('does not create personal books for Not now', async () => {
    const saved = JSON.parse(localStorage.getItem('accountgo.createOrgDraft.v2')!); saved.state.addPersonalBooks = false;
    localStorage.setItem('accountgo.createOrgDraft.v2', JSON.stringify(saved));
    await submit(); await screen.findByText('Your workspace is ready');
    expect(calls).toEqual(['/api/organizations']);
  });
  it('keeps initialized work success when resolving the companion company id loses connection', async () => {
    params.set('continue', 'work');
    const original = authFetch.getMockImplementation()!;
    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url === '/api/organizations/current') throw new Error('Offline');
      return original(url, init);
    });
    await submit();
    expect(await screen.findByText("Your workspace is ready. Personal books weren't created — add them from the workspace switcher.")).toBeTruthy();
  });
  it('creates the companion after signup initialization using the current company id', async () => {
    params.set('continue', 'work'); await submit(); await screen.findByText('Your workspace is ready');
    expect(calls).toEqual(['/api/organizations/current/initialize', '/api/organizations/current/accounting-policy', '/api/organizations/personal']);
    const personal = authFetch.mock.calls.find(([url]) => url === '/api/organizations/personal')!;
    expect(JSON.parse(personal[1].body).linkWorkCompanyId).toBe(42);
  });
  it('opens personal-only setup without a link checkbox and navigates to its dashboard', async () => {
    localStorage.clear(); render(<CreateOrgPage />);
    fireEvent.click(await screen.findByRole('button', { name: 'Only want to track personal money?' }));
    expect(await screen.findByRole('heading', { name: 'Add personal books' })).toBeTruthy();
    expect(screen.queryByRole('checkbox')).toBeNull();
    expect(screen.getByLabelText('Currency')).toHaveProperty('value', 'USD');
    fireEvent.click(screen.getByRole('button', { name: 'Create personal books' }));
    await waitFor(() => expect(navigate).toHaveBeenCalledWith('/org/pat-personal/dashboard', { replace: true }));
    expect(localStorage.getItem('accountgo.createOrgDraft.v2')).toBeNull();
  });
});
