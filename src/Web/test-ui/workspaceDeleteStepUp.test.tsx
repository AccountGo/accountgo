import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { PendingCheckoutScreen } from '../src/components/PendingCheckoutScreen';

const { authFetch, completeStepUp, navigate } = vi.hoisted(() => ({
  authFetch: vi.fn(),
  completeStepUp: vi.fn(),
  navigate: vi.fn(),
}));

vi.mock('../src/auth/AuthContext', () => ({
  useAuth: () => ({ authFetch, completeStepUp }),
}));

vi.mock('react-router-dom', () => ({
  Link: ({ children }: { children: unknown }) => children,
  useNavigate: () => navigate,
  useSearchParams: () => [new URLSearchParams()],
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
});
