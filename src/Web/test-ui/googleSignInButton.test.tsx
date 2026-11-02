import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { GoogleSignInButton } from '../src/components/GoogleSignInButton';

afterEach(() => { cleanup(); delete window.google; });

describe('Google sign-in button', () => {
  it('shows a disabled button before terms are accepted and enables Google after acceptance', async () => {
    const callback = vi.fn();
    let googleCallback: ((response: { credential: string }) => void) | undefined;
    window.google = { accounts: { id: {
      initialize: ({ callback: cb }) => { googleCallback = cb; },
      renderButton: element => { element.textContent = 'Google rendered'; },
    } } };
    const { rerender } = render(<GoogleSignInButton clientId="client" enabled={false} onCredential={callback} onError={vi.fn()} />);
    expect((screen.getByRole('button', { name: 'Continue with Google' }) as HTMLButtonElement).disabled).toBe(true);
    rerender(<GoogleSignInButton clientId="client" enabled={true} onCredential={callback} onError={vi.fn()} />);
    await waitFor(() => expect(screen.getByText('Google rendered')).toBeTruthy());
    googleCallback?.({ credential: 'token' });
    expect(callback).toHaveBeenCalledTimes(1);
    rerender(<GoogleSignInButton clientId="client" enabled={false} onCredential={callback} onError={vi.fn()} />);
    googleCallback?.({ credential: 'late-token' });
    expect(callback).toHaveBeenCalledTimes(1);
  });
});
