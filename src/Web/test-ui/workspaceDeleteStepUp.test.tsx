import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { PendingCheckoutScreen } from '../src/components/PendingCheckoutScreen';

const { authFetch, completeStepUp, navigate, searchParamsState } = vi.hoisted(() => ({
  authFetch: vi.fn(),
  completeStepUp: vi.fn(),
  navigate: vi.fn(),
  searchParamsState: { value: new URLSearchParams() },
}));

vi.mock('../src/auth/AuthContext', () => ({
  useAuth: () => ({ authFetch, completeStepUp }),
}));

vi.mock('react-router-dom', () => ({
  Link: ({ children, to, className }: { children: unknown; to: string; className?: string }) => (
    <a href={to} className={className}>{children as never}</a>
  ),
  useNavigate: () => navigate,
  useSearchParams: () => [searchParamsState.value],
}));

vi.mock('../src/lib/webauthn', () => ({
  isWebAuthnSupported: () => false,
  getPasskeyAssertion: vi.fn(),
}));

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
  searchParamsState.value = new URLSearchParams();
});

describe('workspace delete with step-up', () => {
  it('asks for an authenticator code on mfa_step_up_required, then retries the delete', async () => {
    let deleteCalls = 0;
    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url === '/api/organizations/current' && init?.method === 'DELETE') {
        deleteCalls += 1;
        return deleteCalls === 1
          ? json(403, { code: 'mfa_step_up_required', detail: 'Step-up authentication is required for this operation.' })
          : new Response(null, { status: 204 });
      }
      if (url === '/api/auth/mfa/step-up/totp') {
        expect(JSON.parse(String(init?.body))).toEqual({ code: '123456' });
        return json(200, { accessToken: 'stepped-up' });
      }
      return json(404, {});
    });

    render(<PendingCheckoutScreen name="Scratch" slug="scratch" onActivated={() => {}} />);
    fireEvent.click(screen.getByRole('button', { name: /Discard workspace/ }));
    fireEvent.change(screen.getByLabelText('Confirm workspace slug'), { target: { value: 'scratch' } });
    fireEvent.submit(screen.getByLabelText('Confirm workspace slug').closest('form')!);

    const code = await screen.findByLabelText('Authenticator code');
    expect(screen.queryByText('Step-up authentication is required for this operation.')).toBeNull();
    fireEvent.change(code, { target: { value: '123456' } });
    fireEvent.click(screen.getByRole('button', { name: /Verify and continue/ }));

    await waitFor(() => expect(navigate).toHaveBeenCalledWith('/select-org', expect.anything()));
    expect(completeStepUp).toHaveBeenCalledWith('stepped-up');
    expect(deleteCalls).toBe(2);
  });

  it('shows the server message for other delete failures without a code prompt', async () => {
    authFetch.mockImplementation(async () => json(409, { detail: 'Transfer billing first.' }));

    render(<PendingCheckoutScreen name="Scratch" slug="scratch" onActivated={() => {}} />);
    fireEvent.click(screen.getByRole('button', { name: /Discard workspace/ }));
    fireEvent.change(screen.getByLabelText('Confirm workspace slug'), { target: { value: 'scratch' } });
    fireEvent.submit(screen.getByLabelText('Confirm workspace slug').closest('form')!);

    expect(await screen.findByText('Transfer billing first.')).toBeTruthy();
    expect(screen.queryByLabelText('Authenticator code')).toBeNull();
  });

  it('shows Choose a plan link when pendingPlan is absent', () => {
    render(<PendingCheckoutScreen name="Scratch" slug="scratch" onActivated={() => {}} />);

    const chooseLink = screen.getByRole('link', { name: 'Choose a plan' });
    expect(chooseLink.getAttribute('href')).toBe('/org/scratch/settings?tab=subscription');
    expect(screen.queryByRole('button', { name: /Resume payment/i })).toBeNull();
  });

  it('shows Resume payment button and Choose a different plan link when pendingPlan is present', async () => {
    const assignSpy = vi.fn();
    Object.defineProperty(window, 'location', {
      configurable: true,
      value: { ...window.location, origin: 'https://app.example', assign: assignSpy },
    });
    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url === '/api/Subscription/update' && init?.method === 'PUT') {
        expect(JSON.parse(String(init.body))).toEqual({
          plan: 'accounting_pro',
          billingInterval: 'month',
          returnUrl: 'https://app.example/org/scratch',
          confirmed: true,
        });
        return json(200, { redirectUrl: 'https://checkout.stripe.com/c/pay/cs_123' });
      }
      return json(404, {});
    });

    render(
      <PendingCheckoutScreen
        name="Scratch"
        slug="scratch"
        pendingPlan="accounting_pro"
        onActivated={() => {}}
      />,
    );

    const diffLink = screen.getByRole('link', { name: 'Choose a different plan' });
    expect(diffLink.getAttribute('href')).toBe('/org/scratch/settings?tab=subscription&upgrade=accounting_pro');

    fireEvent.click(screen.getByRole('button', { name: 'Resume payment' }));
    await waitFor(() => expect(assignSpy).toHaveBeenCalledWith('https://checkout.stripe.com/c/pay/cs_123'));
  });

  it('auto-resumes checkout and strips ?resume=1 on mount when resume=1&interval=year', async () => {
    searchParamsState.value = new URLSearchParams('resume=1&interval=year');
    const assignSpy = vi.fn();
    const replaceStateSpy = vi.spyOn(window.history, 'replaceState').mockImplementation(() => {});
    Object.defineProperty(window, 'location', {
      configurable: true,
      value: {
        ...window.location,
        origin: 'https://app.example',
        href: 'https://app.example/org/scratch?resume=1&interval=year',
        assign: assignSpy,
      },
    });
    authFetch.mockImplementation(async (url: string, init?: RequestInit) => {
      if (url === '/api/Subscription/update' && init?.method === 'PUT') {
        expect(JSON.parse(String(init.body))).toEqual({
          plan: 'accounting_pro',
          billingInterval: 'year',
          returnUrl: 'https://app.example/org/scratch',
          confirmed: true,
        });
        return json(200, { redirectUrl: 'https://checkout.stripe.com/c/pay/cs_year' });
      }
      return json(404, {});
    });

    render(
      <PendingCheckoutScreen
        name="Scratch"
        slug="scratch"
        pendingPlan="accounting_pro"
        onActivated={() => {}}
      />,
    );

    await waitFor(() => expect(replaceStateSpy).toHaveBeenCalledWith({}, '', '/org/scratch?interval=year'));
    await waitFor(() => expect(assignSpy).toHaveBeenCalledWith('https://checkout.stripe.com/c/pay/cs_year'));
    replaceStateSpy.mockRestore();
  });
});
