import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { Login } from '../src/pages/Login';
import { GoogleSignInButton } from '../src/components/GoogleSignInButton';

const authContextMock = {
  isAuthenticated: false,
  isLoading: false,
  sendOtp: vi.fn().mockResolvedValue({ success: true }),
  signUp: vi.fn().mockResolvedValue({ success: true }),
  verifyOtp: vi.fn().mockResolvedValue({ success: true }),
  completeSession: vi.fn(),
  authFetch: vi.fn(),
};

vi.mock('../src/auth/AuthContext', () => ({
  useAuth: () => authContextMock,
}));

vi.mock('../src/components/TurnstileWidget', () => ({
  TurnstileWidget: () => <div data-testid="turnstile-widget" />,
}));

vi.mock('../src/components/mfa/MfaChallenge', () => ({
  MfaChallenge: ({ onCancel }: { onCancel: () => void }) => (
    <div data-testid="mfa-challenge">
      <span>MFA Challenge Screen</span>
      <button type="button" onClick={onCancel}>Cancel MFA</button>
    </div>
  ),
}));

vi.mock('../src/components/mfa/MfaEnrollWizard', () => ({
  MfaEnrollWizard: ({ onCancel }: { onCancel: () => void }) => (
    <div data-testid="mfa-enroll">
      <span>MFA Enrollment Screen</span>
      <button type="button" onClick={onCancel}>Cancel Enroll</button>
    </div>
  ),
}));

describe('Google Sign-In UX (U1 - U9)', () => {
  const originalFetch = global.fetch;

  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    cleanup();
    global.fetch = originalFetch;
    delete (window as unknown as { google?: unknown }).google;
  });

  // U1: /start disabled or failing: no button, no divider; OTP works.
  it('U1: when /start is disabled or fails, Google button and divider are hidden and OTP works', async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ enabled: false }), { status: 200, headers: { 'Content-Type': 'application/json' } })
    );

    render(
      <MemoryRouter initialEntries={['/login']}>
        <Login initialMode="signin" />
      </MemoryRouter>
    );

    // Divider "or" and Continue with Google should not exist
    await waitFor(() => {
      expect(screen.queryByText(/continue with google/i)).toBeNull();
      expect(screen.queryByText('or')).toBeNull();
    });

    // Email OTP form is visible and usable
    const emailInput = screen.getByLabelText(/work email/i);
    expect(emailInput).toBeTruthy();
    const submitBtn = screen.getByRole('button', { name: /send verification code/i });
    expect(submitBtn).toBeTruthy();

    const termsCheckbox = screen.getByRole('checkbox');
    fireEvent.click(termsCheckbox);
    fireEvent.change(emailInput, { target: { value: 'user@example.com' } });
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(authContextMock.sendOtp).toHaveBeenCalled();
    });
  });

  // U2: The nonce from /start is passed to initialize.
  it('U2: passes the nonce from /start to google.accounts.id.initialize', async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ enabled: true, clientId: 'client-123.apps.googleusercontent.com', nonce: 'nonce-u2-secret' }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    );

    const initFn = vi.fn();
    (window as unknown as { google: unknown }).google = {
      accounts: {
        id: {
          initialize: initFn,
          renderButton: (el: HTMLElement) => {
            el.textContent = 'Google Rendered';
          },
        },
      },
    };

    render(
      <MemoryRouter initialEntries={['/login']}>
        <Login initialMode="signin" />
      </MemoryRouter>
    );

    // Accept terms to enable the button and trigger initialize
    await waitFor(() => expect(screen.getByRole('checkbox')).toBeTruthy());
    fireEvent.click(screen.getByRole('checkbox'));

    await waitFor(() => {
      expect(initFn).toHaveBeenCalledWith(
        expect.objectContaining({
          client_id: 'client-123.apps.googleusercontent.com',
          nonce: 'nonce-u2-secret',
        })
      );
    });
  });

  // U3: Signup mode shows no button and shows the sign-in hint.
  it('U3: signup mode shows no Google button and shows the sign-in hint that switches mode', async () => {
    global.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ enabled: true, clientId: 'client-123', nonce: 'nonce-123' }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    );

    (window as unknown as { google: unknown }).google = {
      accounts: {
        id: {
          initialize: vi.fn(),
          renderButton: (el: HTMLElement) => { el.textContent = 'Google Rendered'; },
        },
      },
    };

    render(
      <MemoryRouter initialEntries={['/signup']}>
        <Login initialMode="signup" />
      </MemoryRouter>
    );

    // No Continue with Google button in signup
    expect(screen.queryByText(/continue with google/i)).toBeNull();

    // Sign-in hint is present once config loads
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /sign in with google/i })).toBeTruthy();
    });
    const hintBtn = screen.getByRole('button', { name: /sign in with google/i });

    // Clicking switches to signin mode
    fireEvent.click(hintBtn);

    // In signin mode, Google button is present
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /continue with google/i })).toBeTruthy();
    });
  });

  // U4: Each code shows its copy and action from section 5.
  it('U4: maps each error code to its copy and action from section 5', async () => {
    let googleCallback: ((resp: { credential: string }) => void) | undefined;
    let clickListener: (() => void) | undefined;

    (window as unknown as { google: unknown }).google = {
      accounts: {
        id: {
          initialize: ({ callback }: { callback: (resp: { credential: string }) => void }) => {
            googleCallback = callback;
          },
          renderButton: (el: HTMLElement, opts: unknown) => {
            el.textContent = 'Google Rendered';
            clickListener = (opts as { click_listener?: () => void })?.click_listener;
          },
        },
      },
    };

    const testErrorCode = async (code: string, expectedText: RegExp, expectedActionLabel?: RegExp) => {
      global.fetch = vi.fn().mockImplementation(async (url: string) => {
        if (url === '/api/auth/google/start') {
          return new Response(JSON.stringify({ enabled: true, clientId: 'client', nonce: 'nonce' }), {
            status: 200,
            headers: { 'Content-Type': 'application/json' },
          });
        }
        if (url === '/api/auth/google') {
          return new Response(JSON.stringify({ code, detail: 'Raw detail from server' }), {
            status: 400,
            headers: { 'Content-Type': 'application/problem+json' },
          });
        }
        return new Response(null, { status: 404 });
      });

      const { unmount } = render(
        <MemoryRouter initialEntries={['/login']}>
          <Login initialMode="signin" />
        </MemoryRouter>
      );

      // Accept terms
      fireEvent.click(screen.getByRole('checkbox'));
      await waitFor(() => expect(screen.getByText('Google Rendered')).toBeTruthy());

      // User initiates Google sign-in
      clickListener?.();

      // Trigger Google credential callback
      googleCallback?.({ credential: 'token-' + code });

      await waitFor(() => {
        expect(screen.getByText(expectedText)).toBeTruthy();
        // Server raw detail is never displayed as is
        expect(screen.queryByText('Raw detail from server')).toBeNull();
      });

      if (expectedActionLabel) {
        expect(screen.getByRole('button', { name: expectedActionLabel })).toBeTruthy();
      }

      unmount();
    };

    // 1. account_not_found -> switches to signup on click
    await testErrorCode('account_not_found', /no accountgo account uses/i, /create an account/i);

    // 2. account_linking_required -> email signin action
    await testErrorCode('account_linking_required', /google can't confirm this address for us/i, /sign in with email/i);

    // 3. google_already_linked -> email code signin action
    await testErrorCode('google_already_linked', /your account is connected to a different google account/i, /sign in with email/i);

    // 4. account_unavailable -> support action
    await testErrorCode('account_unavailable', /this account can't sign in\. contact support\./i);

    // 5. token_replayed / nonce_* / invalid_token / cross_site -> expired sign-in
    await testErrorCode('token_replayed', /that sign-in expired\. try again\./i);
    await testErrorCode('nonce_mismatch', /that sign-in expired\. try again\./i);

    // 6. google_unavailable / unexpected_error -> unavailable
    await testErrorCode('google_unavailable', /google sign-in is unavailable right now\. use your email code\./i);
    await testErrorCode('unexpected_error', /google sign-in is unavailable right now\. use your email code\./i);
  }, 20000);

  // U5: After any failure, /start is called again before the button is enabled.
  it('U5: re-calls /start after a failed Google sign-in to refresh nonce', async () => {
    let googleCallback: ((resp: { credential: string }) => void) | undefined;
    let clickListener: (() => void) | undefined;

    (window as unknown as { google: unknown }).google = {
      accounts: {
        id: {
          initialize: ({ callback }: { callback: (resp: { credential: string }) => void }) => {
            googleCallback = callback;
          },
          renderButton: (el: HTMLElement, opts: unknown) => {
            el.textContent = 'Google Rendered';
            clickListener = (opts as { click_listener?: () => void })?.click_listener;
          },
        },
      },
    };

    let startCallCount = 0;
    global.fetch = vi.fn().mockImplementation(async (url: string) => {
      if (url === '/api/auth/google/start') {
        startCallCount++;
        return new Response(JSON.stringify({ enabled: true, clientId: 'client', nonce: `nonce-${startCallCount}` }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        });
      }
      if (url === '/api/auth/google') {
        return new Response(JSON.stringify({ code: 'token_replayed' }), {
          status: 401,
          headers: { 'Content-Type': 'application/problem+json' },
        });
      }
      return new Response(null, { status: 404 });
    });

    render(
      <MemoryRouter initialEntries={['/login']}>
        <Login initialMode="signin" />
      </MemoryRouter>
    );

    // Accept terms
    fireEvent.click(screen.getByRole('checkbox'));
    await waitFor(() => expect(screen.getByText('Google Rendered')).toBeTruthy());
    expect(startCallCount).toBe(1);

    // User initiates Google sign-in
    clickListener?.();

    // Trigger failed credential
    googleCallback?.({ credential: 'tok' });

    // Expect /start to be called again to refresh nonce
    await waitFor(() => {
      expect(startCallCount).toBeGreaterThanOrEqual(2);
    });
  });

  // U6: Cancel in the overlay restores the form with no request sent.
  it('U6: clicking cancel in the overlay restores the form with no request sent', async () => {
    let clickListener: (() => void) | undefined;
    (window as unknown as { google: unknown }).google = {
      accounts: {
        id: {
          initialize: () => {},
          renderButton: (_el: HTMLElement, opts: unknown) => {
            clickListener = (opts as { click_listener?: () => void })?.click_listener;
          },
        },
      },
    };

    const fetchMock = vi.fn().mockImplementation(async (url: string) => {
      if (url === '/api/auth/google/start') {
        return new Response(JSON.stringify({ enabled: true, clientId: 'client', nonce: 'nonce' }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        });
      }
      return new Response(null, { status: 200 });
    });
    global.fetch = fetchMock;

    render(
      <MemoryRouter initialEntries={['/login']}>
        <Login initialMode="signin" />
      </MemoryRouter>
    );

    // Accept terms
    fireEvent.click(screen.getByRole('checkbox'));

    // Wait for renderButton to have run and assigned clickListener
    await waitFor(() => expect(clickListener).toBeDefined());

    // User clicks Google sign in
    clickListener?.();

    // Overlay is visible
    await waitFor(() => {
      expect(screen.getByText(/continue with google/i)).toBeTruthy();
      expect(screen.getByRole('button', { name: /cancel/i })).toBeTruthy();
    });

    // Click Cancel in overlay
    const cancelBtn = screen.getByRole('button', { name: /cancel/i });
    fireEvent.click(cancelBtn);

    // Overlay is dismissed
    await waitFor(() => {
      expect(screen.queryByRole('button', { name: /cancel/i })).toBeNull();
    });

    // POST /api/auth/google was never called
    expect(fetchMock).not.toHaveBeenCalledWith('/api/auth/google', expect.anything());
  });

  // U7: GIS script load failure shows the fallback message.
  it('U7: GIS script load failure triggers error fallback message', async () => {
    const onError = vi.fn();
    render(
      <GoogleSignInButton
        clientId="client"
        enabled={true}
        onCredential={vi.fn()}
        onError={onError}
      />
    );

    // Find script added by GoogleSignInButton
    const script = document.querySelector('script[src*="gsi/client"]') as HTMLScriptElement;
    expect(script).toBeTruthy();

    // Simulate script load error
    script.onerror?.(new Event('error'));
    expect(onError).toHaveBeenCalledWith('Google sign-in could not be loaded. Please use your email code.');
  });

  // U8: Terms unchecked keeps the button disabled.
  it('U8: terms unchecked keeps button disabled and explains terms acceptance requirement', async () => {
    (window as unknown as { google: unknown }).google = {
      accounts: {
        id: {
          initialize: vi.fn(),
          renderButton: (el: HTMLElement) => { el.textContent = 'Google Rendered'; },
        },
      },
    };

    global.fetch = vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ enabled: true, clientId: 'client', nonce: 'nonce' }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    );

    render(
      <MemoryRouter initialEntries={['/login']}>
        <Login initialMode="signin" />
      </MemoryRouter>
    );

    // Checkbox unchecked initially
    const termsCheckbox = screen.getByRole('checkbox') as HTMLInputElement;
    expect(termsCheckbox.checked).toBe(false);

    // Terms notice tooltip and disabled button are shown
    const btn = await screen.findByRole('button', { name: /continue with google/i }) as HTMLButtonElement;
    expect(btn.disabled).toBe(true);
    expect(screen.getByText(/please accept the privacy policy and terms of use checkbox to continue/i)).toBeTruthy();

    // Checking terms enables Google
    fireEvent.click(termsCheckbox);
    expect(termsCheckbox.checked).toBe(true);

    await waitFor(() => {
      expect(screen.getByText('Google Rendered')).toBeTruthy();
    });
  });

  // U9: MFA challenge and enrollment steps after Google go back to the right step.
  it('U9: MFA challenge and enrollment steps after Google cancel back to step 1 (sign-in form)', async () => {
    let googleCallback: ((resp: { credential: string }) => void) | undefined;
    let clickListener: (() => void) | undefined;

    (window as unknown as { google: unknown }).google = {
      accounts: {
        id: {
          initialize: ({ callback }: { callback: (resp: { credential: string }) => void }) => {
            googleCallback = callback;
          },
          renderButton: (el: HTMLElement, opts: unknown) => {
            el.textContent = 'Google Rendered';
            clickListener = (opts as { click_listener?: () => void })?.click_listener;
          },
        },
      },
    };

    // 1. Test MFA Challenge cancel
    global.fetch = vi.fn().mockImplementation(async (url: string) => {
      if (url === '/api/auth/google/start') {
        return new Response(JSON.stringify({ enabled: true, clientId: 'client', nonce: 'nonce' }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        });
      }
      if (url === '/api/auth/google') {
        return new Response(
          JSON.stringify({
            status: 'mfa_required',
            mfaToken: 'test-mfa-token',
            methods: ['totp'],
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }
      return new Response(null, { status: 404 });
    });

    const { unmount } = render(
      <MemoryRouter initialEntries={['/login']}>
        <Login initialMode="signin" />
      </MemoryRouter>
    );

    fireEvent.click(screen.getByRole('checkbox'));
    await waitFor(() => expect(screen.getByText('Google Rendered')).toBeTruthy());

    clickListener?.();
    googleCallback?.({ credential: 'tok' });

    // Enters MFA challenge
    await waitFor(() => {
      expect(screen.getByTestId('mfa-challenge')).toBeTruthy();
    });

    // Clicking cancel routes back to step 1 (sign in form, not OTP code form)
    fireEvent.click(screen.getByRole('button', { name: /cancel mfa/i }));
    await waitFor(() => {
      expect(screen.queryByTestId('mfa-challenge')).toBeNull();
      expect(screen.getByLabelText(/work email/i)).toBeTruthy();
    });
    unmount();

    // 2. Test MFA Enrollment cancel
    global.fetch = vi.fn().mockImplementation(async (url: string) => {
      if (url === '/api/auth/google/start') {
        return new Response(JSON.stringify({ enabled: true, clientId: 'client', nonce: 'nonce' }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        });
      }
      if (url === '/api/auth/google') {
        return new Response(
          JSON.stringify({
            status: 'mfa_enrollment_required',
            mfaToken: 'test-enroll-token',
            methods: ['totp'],
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } }
        );
      }
      return new Response(null, { status: 404 });
    });

    render(
      <MemoryRouter initialEntries={['/login']}>
        <Login initialMode="signin" />
      </MemoryRouter>
    );

    fireEvent.click(screen.getByRole('checkbox'));
    await waitFor(() => expect(screen.getByText('Google Rendered')).toBeTruthy());

    clickListener?.();
    googleCallback?.({ credential: 'tok' });

    // Enters MFA enroll
    await waitFor(() => {
      expect(screen.getByTestId('mfa-enroll')).toBeTruthy();
    });

    // Clicking cancel routes back to step 1 (sign in form)
    fireEvent.click(screen.getByRole('button', { name: /cancel enroll/i }));
    await waitFor(() => {
      expect(screen.queryByTestId('mfa-enroll')).toBeNull();
      expect(screen.getByLabelText(/work email/i)).toBeTruthy();
    });
  });
});
