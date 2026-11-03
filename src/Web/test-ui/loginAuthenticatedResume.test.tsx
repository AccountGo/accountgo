import { cleanup, render, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

// Mounted-component regression for the already-authenticated resume effect in Login. It must route an
// uninitialized signup workspace into continuation, resolved from trusted server state (not straight to
// a dashboard), which is exactly the bypass finding 2 reproduced.

const { authFetch, navigate, auth, locationState } = vi.hoisted(() => ({
  authFetch: vi.fn(),
  navigate: vi.fn(),
  auth: {
    isAuthenticated: true,
    isLoading: false,
    isStarting: false,
  } as Record<string, unknown>,
  locationState: { value: {} as unknown },
}));

vi.mock('../src/auth/AuthContext', () => ({
  useAuth: () => ({ ...auth, authFetch }),
}));

vi.mock('react-router-dom', () => ({
  useNavigate: () => navigate,
  useLocation: () => ({ pathname: '/login', search: '', state: locationState.value }),
}));

// Heavy child components the resume effect does not depend on — stub them out so Login mounts cleanly.
vi.mock('../src/components/GoogleSignInButton', () => ({ GoogleSignInButton: () => null }));
vi.mock('../src/components/mfa/MfaChallenge', () => ({ MfaChallenge: () => null }));
vi.mock('../src/components/mfa/MfaEnrollWizard', () => ({ MfaEnrollWizard: () => null }));
vi.mock('../src/components/TurnstileWidget', () => ({ TurnstileWidget: () => null, TurnstileWidgetHandle: {} }));

const mockStorage = (() => {
  let store: Record<string, string> = {};
  return {
    getItem: (k: string) => store[k] ?? null,
    setItem: (k: string, v: string) => { store[k] = String(v); },
    removeItem: (k: string) => { delete store[k]; },
    clear: () => { store = {}; },
  };
})();
if (typeof window !== 'undefined') {
  Object.defineProperty(window, 'localStorage', { value: mockStorage, writable: true });
}

beforeEach(() => {
  mockStorage.clear();
  navigate.mockReset();
  authFetch.mockReset();
  locationState.value = {};
});

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

function serverWith(needsSetup: Record<string, boolean>, orgs: Array<{ slug: string }> = []) {
  authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
    if (url === '/api/organizations') {
      return new Response(JSON.stringify(orgs), { status: 200, headers: { 'Content-Type': 'application/json' } });
    }
    if (url === '/api/organizations/current') {
      const slug = new Headers(init?.headers).get('X-AccountGo-Org') ?? '';
      const pending = needsSetup[slug] === true;
      // Routing follows the server-derived pendingSignupContinuation capability, not the raw state string.
      const policy = {
        operatingPolicy: {
          collaborationState: pending ? 'LegacyUnclassified' : 'SoloActive',
          pendingSignupContinuation: pending,
        },
      };
      return new Response(JSON.stringify(policy), { status: 200, headers: { 'Content-Type': 'application/json' } });
    }
    return new Response(null, { status: 404 });
  });
}

describe('Login already-authenticated resume routes through the shared continuation resolver', () => {
  it('session restored with a persisted uninitialized workspace routes to continuation (not the dashboard)', async () => {
    mockStorage.setItem('accountgo:lastOrgSlug', 'pending-books'); // the persisted slug from a prior visit
    serverWith({ 'pending-books': true });

    const { Login } = await import('../src/pages/Login');
    render(<Login initialMode="signin" />);

    await waitFor(() => {
      expect(navigate).toHaveBeenCalledWith('/create-org?continue=pending-books', { replace: true });
    });
    // It must NOT have navigated straight to the uninitialized dashboard.
    expect(navigate).not.toHaveBeenCalledWith('/org/pending-books/dashboard', { replace: true });
  });

  it('session restored with an initialized workspace routes to the dashboard', async () => {
    mockStorage.setItem('accountgo:lastOrgSlug', 'ready-books');
    serverWith({ 'ready-books': false });

    const { Login } = await import('../src/pages/Login');
    render(<Login initialMode="signin" />);

    await waitFor(() => {
      expect(navigate).toHaveBeenCalledWith('/org/ready-books/dashboard', { replace: true });
    });
    expect(navigate).not.toHaveBeenCalledWith('/create-org?continue=ready-books', { replace: true });
  });
});
