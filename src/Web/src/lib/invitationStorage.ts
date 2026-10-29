const INVITATION_TOKEN_KEY = 'accountgo:invitationToken';

export function storeInvitationToken(token: string): void {
  try {
    sessionStorage.setItem(INVITATION_TOKEN_KEY, token);
  } catch {
    // The current navigation still carries the token when session storage is unavailable.
  }
}

export function getInvitationToken(): string | null {
  try {
    return sessionStorage.getItem(INVITATION_TOKEN_KEY);
  } catch {
    return null;
  }
}

export function clearInvitationToken(): void {
  try {
    sessionStorage.removeItem(INVITATION_TOKEN_KEY);
  } catch {
    // Ignore storage errors in restricted contexts.
  }
}
