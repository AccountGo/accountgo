import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router-dom';
import { OrgSwitcher } from '../src/components/OrgSwitcher';
import { Sidebar } from '../src/components/Sidebar';
import { MobileBottomNav } from '../src/components/MobileBottomNav';
import type { OrgDetails } from '../src/types';

const { authFetch } = vi.hoisted(() => ({ authFetch: vi.fn() }));
vi.mock('../src/auth/AuthContext', () => ({ useAuth: () => ({
  user: { id: 'owner', firstName: 'Pat', email: 'pat@example.test' }, authFetch,
}) }));
vi.mock('../src/themes/ThemeContext', () => ({ useTheme: () => ({ theme: 'light', setTheme: vi.fn() }) }));

const work: OrgDetails = { id: '1', name: 'Consulting', slug: 'consulting', currency: 'EUR', role: 'Owner',
  linkedPersonalCompanyId: 2, canAddPersonalBooks: false };
const personal: OrgDetails = { id: '2', name: 'Pat', slug: 'pat-personal', isPersonalBooks: true,
  linkedWorkCompanyIds: [1], canAddPersonalBooks: false };
const other: OrgDetails = { id: '3', name: 'Other', slug: 'other', canAddPersonalBooks: false };
function Location() { return <output data-testid="location">{useLocation().pathname}</output>; }
afterEach(() => { cleanup(); vi.clearAllMocks(); });

describe('Personal books switcher', () => {
  it('groups linked work and personal books and opens personal on its dashboard', () => {
    render(<MemoryRouter initialEntries={['/org/consulting/sales']}>
      <OrgSwitcher currentOrg={work} availableOrgs={[work, other, personal]} /><Location />
    </MemoryRouter>);
    fireEvent.click(screen.getByRole('button', { expanded: false }));
    const items = screen.getAllByRole('menuitem').map(item => item.textContent);
    expect(items[0]).toContain('Consulting · Work');
    expect(items[1]).toContain('Pat · Personal');
    expect(items[2]).toContain('Other');
    fireEvent.click(screen.getByRole('menuitem', { name: /Pat · Personal/ }));
    expect(screen.getByTestId('location').textContent).toBe('/org/pat-personal/dashboard');
  });

  it.each(['Juan — Personal', 'Juan — PERSONAL', 'Juan — personal'])('keeps the existing personal label in %s', name => {
    const books = { ...personal, name };
    render(<MemoryRouter><OrgSwitcher currentOrg={books} availableOrgs={[work, books]} /></MemoryRouter>);
    expect(screen.getByRole('button', { expanded: false }).textContent).toContain(name);
    expect(screen.getByRole('button', { expanded: false }).textContent).not.toContain(' · Personal');
    fireEvent.click(screen.getByRole('button', { expanded: false }));
    expect(screen.getByRole('menuitem', { name: new RegExp(name) }).textContent).not.toContain(' · Personal');
    expect(screen.getByRole('menuitem', { name: /Consulting · Work/ })).toBeTruthy();
  });

  it('submits personal books with owner defaults and switches to the created workspace', async () => {
    authFetch.mockResolvedValue(new Response(JSON.stringify({ id: '4', slug: 'pat-home' }), { status: 201 }));
    render(<MemoryRouter initialEntries={['/org/consulting/sales']}>
      <OrgSwitcher currentOrg={{ ...work, canAddPersonalBooks: true, linkedPersonalCompanyId: undefined }}
        availableOrgs={[{ ...work, canAddPersonalBooks: true }]} /><Location />
    </MemoryRouter>);
    fireEvent.click(screen.getByRole('button', { expanded: false }));
    fireEvent.click(screen.getByRole('menuitem', { name: 'Add personal books' }));
    expect((screen.getByLabelText('Name') as HTMLInputElement).value).toBe('Pat — Personal');
    expect((screen.getByLabelText('Currency') as HTMLInputElement).value).toBe('EUR');
    expect((screen.getByLabelText('Link to Consulting') as HTMLInputElement).checked).toBe(true);
    fireEvent.click(screen.getByRole('button', { name: 'Create personal books' }));
    await waitFor(() => expect(screen.getByTestId('location').textContent).toBe('/org/pat-home/dashboard'));
    const [url, request] = authFetch.mock.calls[0];
    expect(url).toBe('/api/organizations/personal');
    expect(JSON.parse(request.body)).toEqual({ name: 'Pat — Personal', currency: 'EUR', linkWorkCompanyId: 1 });
  });

  it('shows server errors without switching or closing the form', async () => {
    authFetch.mockResolvedValue(new Response(JSON.stringify({ detail: 'You already have personal books. Each person can have one.' }), { status: 409 }));
    render(<MemoryRouter><OrgSwitcher currentOrg={{ ...work, canAddPersonalBooks: true }} availableOrgs={[{ ...work, canAddPersonalBooks: true }]} /></MemoryRouter>);
    fireEvent.click(screen.getByRole('button', { expanded: false }));
    fireEvent.click(screen.getByRole('menuitem', { name: 'Add personal books' }));
    fireEvent.click(screen.getByRole('button', { name: 'Create personal books' }));
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', 'You already have personal books. Each person can have one.');
    expect(screen.getByRole('dialog')).toBeTruthy();
  });

  it('hides sales navigation when the sales module is absent', () => {
    const hasModule = (key: string) => ['financials', 'purchases', 'reports'].includes(key);
    render(<MemoryRouter><Sidebar currentOrg={personal} availableOrgs={[personal]} user={{ id: 'owner', name: 'Pat', email: 'pat@example.test', role: 'Owner' }}
      hasModule={hasModule} hasPermission={() => true} />
      <MobileBottomNav currentOrg={personal} onOpenMenu={() => {}} hasModule={hasModule} hasPermission={() => true} /></MemoryRouter>);
    expect(document.querySelector('a[href="/org/pat-personal/sales"]')).toBeNull();
    expect(document.querySelector('a[href="/org/pat-personal/purchases"]')).not.toBeNull();
    expect(screen.getAllByText('Dashboard').length).toBeGreaterThan(0);
  });
});
