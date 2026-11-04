import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { Invitation } from '../src/pages/Invitation';

const VALID_TOKEN = 'a'.repeat(43);

vi.mock('react-router-dom', () => ({
  useParams: () => ({ token: VALID_TOKEN }),
  useNavigate: () => vi.fn(),
  Link: ({ children }: { children: React.ReactNode }) => <a>{children}</a>,
}));
vi.mock('../src/auth/AuthContext', () => ({
  useAuth: () => ({ authFetch: vi.fn(), isAuthenticated: false, user: null, logout: vi.fn() }),
}));

function previewResponse(access: object | undefined) {
  return new Response(JSON.stringify({
    workspaceName: 'Acme Books',
    inviterName: 'Olive Owner',
    role: 'Member',
    emailMasked: 'i***@example.com',
    expired: false,
    accepted: false,
    ...(access ? { access } : {}),
  }), { status: 200 });
}

beforeEach(() => { vi.stubGlobal('fetch', vi.fn()); });
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

it('shows the functional access the invitee will receive before they accept', async () => {
  vi.mocked(fetch).mockResolvedValue(previewResponse({
    roles: ['Auditor / Viewer'],
    areas: [{ area: 'reports', label: 'Reports', level: 'View only' }],
  }));
  render(<Invitation />);

  expect(await screen.findByText('What you will be able to do')).toBeDefined();
  expect(screen.getByText('Access: Auditor / Viewer')).toBeDefined();
  expect(screen.getByText('Reports: View only')).toBeDefined();
});

it('says plainly when the invitation carries no functional access', async () => {
  vi.mocked(fetch).mockResolvedValue(previewResponse({ roles: [], areas: [] }));
  render(<Invitation />);

  expect(await screen.findByText(/no functional access yet/)).toBeDefined();
});
