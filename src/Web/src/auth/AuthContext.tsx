import React, { createContext, useContext, useState, useEffect, useRef, useCallback } from 'react';
import { UserDto, AuthResponse, OtpSendResponse, SignUpPayload, SignUpResponse, AuthContextType, VerifyOtpResult, MfaRequiredResponse, MfaEnrollmentRequiredResponse } from '../types/auth';
import { announceEntitlementDenial } from '../lib/entitlementEvents';
import { ENTITLEMENTS_VERSION_HEADER, observeEntitlementsVersionHeader } from '../lib/entitlementVersion';
import { startIdleSession, type IdleSession, type SessionWarning, type SignOutReason } from '../lib/idleSession';
import { SessionTimeoutDialog } from '../components/SessionTimeoutDialog';
import {
  parseResponseServiceProblem,
  reportNetworkFailure,
  reportServiceProblem,
  reportServiceRestored,
  shouldRetry503,
} from '../lib/serviceStatus';

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export const ORG_HEADER = 'X-AccountGo-Org';

// Reads the workspace slug from the current URL (/org/:orgSlug/...). Returns null outside a workspace.
export function getOrgSlugFromLocation(pathname: string = window.location.pathname): string | null {
  const match = /^\/org\/([^/?#]+)/.exec(pathname);
  if (!match) return null;
  try {
    return decodeURIComponent(match[1]);
  } catch {
    return null;
  }
}

export async function fetchWith503Retry(
  input: RequestInfo | URL,
  init?: RequestInit,
  maxRetries = 3,
  options?: { userWrite?: boolean }
): Promise<Response> {
  const method = (init?.method || 'GET').toUpperCase();
  let attempt = 0;
  while (true) {
    const res = await fetch(input, init);
    if (res.status === 503) {
      const problem = await parseResponseServiceProblem(res.clone());
      const canRetry = shouldRetry503(method, problem);
      if (canRetry && attempt < maxRetries) {
        attempt++;
        const retryAfterHeader = res.headers.get('Retry-After');
        let delaySec = retryAfterHeader ? parseInt(retryAfterHeader, 10) : 5;
        if (isNaN(delaySec) || delaySec <= 0) delaySec = 5;
        delaySec = Math.min(delaySec, 10);
        await new Promise((resolve) => setTimeout(resolve, delaySec * 1000));
        continue;
      }
      if (problem) {
        reportServiceProblem(problem, { method, userWrite: options?.userWrite });
      }
    }
    return res;
  }
}

// How long to keep retrying a refresh while the API is starting before giving up on the session. A cold start on the
// smallest container can take several minutes.
const REFRESH_STARTUP_DEADLINE_MS = 15 * 60 * 1000;

/** Lock name shared across bundle versions — do not rename (old/new tabs must coordinate during rollout). */
export const SESSION_REFRESH_LOCK_NAME = 'accountgo-session-refresh';

// Only one tab refreshes at a time. Refresh tokens are single-use and every tab shares the cookie, so two tabs
// refreshing together would present the same token twice. After the lock, the next tab sends the already-rotated
// cookie and does a normal rotation (tokens stay in memory only — never localStorage / BroadcastChannel).
export function withRefreshLock<T>(work: () => Promise<T>): Promise<T> {
  const locks = typeof navigator !== 'undefined' ? (navigator as Navigator & { locks?: LockManager }).locks : undefined;
  return locks ? locks.request(SESSION_REFRESH_LOCK_NAME, work) : work();
}

const wait = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

function describeDuration(ms: number): string {
  const minutes = Math.round(ms / 60_000);
  if (minutes < 60 || minutes % 60 !== 0) return `${minutes} minutes`;
  const hours = minutes / 60;
  return hours === 1 ? '1 hour' : `${hours} hours`;
}

export function signedOutNotice(reason: SignOutReason, idleMs: number): string {
  return reason === 'idle'
    ? `You were signed out after ${describeDuration(idleMs)} of inactivity. Sign in to continue where you left off.`
    : 'For your security, your session reached its time limit. Sign in to continue where you left off.';
}

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [user, setUser] = useState<UserDto | null>(null);
  const [accessToken, setAccessToken] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [isStarting, setIsStarting] = useState<boolean>(false);
  // Automatic sign-out. Plain numbers so a routine refresh with the same limits doesn't restart the idle clock.
  const [idleMs, setIdleMs] = useState<number | null>(null);
  const [sessionExpiresAt, setSessionExpiresAt] = useState<number | null>(null);
  const [sessionWarning, setSessionWarning] = useState<SessionWarning>(null);
  const [signedOutNoticeText, setSignedOutNoticeText] = useState<string | null>(null);
  const idleSessionRef = useRef<IdleSession | null>(null);
  const idleMsRef = useRef<number | null>(null);
  idleMsRef.current = idleMs;
  const refreshedAtRef = useRef<number>(0);
  const lastRefreshedOrgSlugRef = useRef<string | null>(null);

  // Ref to hold current token synchronously for fetch interceptor & refresh loops
  const tokenRef = useRef<string | null>(null);
  tokenRef.current = accessToken;

  // Single-flight refresh promise to prevent multiple concurrent refresh calls
  const refreshPromiseRef = useRef<Promise<string | null> | null>(null);

  // Adopt the server's sign-out limits from any sign-in or refresh response.
  const applySessionLimits = useCallback((data: AuthResponse) => {
    const nextIdleMs =
      typeof data.idleTimeoutSeconds === 'number' && data.idleTimeoutSeconds > 0
        ? data.idleTimeoutSeconds * 1000
        : null;
    refreshedAtRef.current = Date.now();
    idleSessionRef.current?.markRefreshed(refreshedAtRef.current, nextIdleMs ?? undefined);
    setIdleMs(nextIdleMs);
    const expires = data.sessionExpiresAt ? Date.parse(data.sessionExpiresAt) : NaN;
    setSessionExpiresAt(Number.isFinite(expires) ? expires : null);
    setSignedOutNoticeText(null);
  }, []);

  // Silent refresh helper
  const performRefresh = useCallback(async (orgSlugIfChanged?: string): Promise<string | null> => {
    const normalizedIfChanged = typeof orgSlugIfChanged === 'string' ? orgSlugIfChanged.trim().toLowerCase() : null;
    if (normalizedIfChanged && lastRefreshedOrgSlugRef.current === normalizedIfChanged) {
      return tokenRef.current;
    }

    if (refreshPromiseRef.current) {
      return refreshPromiseRef.current;
    }

    refreshPromiseRef.current = withRefreshLock(async () => {
      // Only a refusal from the API ends the session. While the API is starting (503, a gateway timeout) or can't be
      // reached, keep the session and try again: ending it then would sign everyone out after every update.
      const deadline = Date.now() + REFRESH_STARTUP_DEADLINE_MS;
      try {
        while (true) {
          let res: Response | null = null;
          const orgSlug = getOrgSlugFromLocation();
          try {
            const headers: Record<string, string> = { 'Content-Type': 'application/json' };
            if (orgSlug) {
              headers[ORG_HEADER] = orgSlug;
            }
            res = await fetch('/api/auth/refresh', {
              method: 'POST',
              headers,
              credentials: 'include',
            });
          } catch {
            res = null;
            reportNetworkFailure({ method: 'POST' });
          }

          if (res?.ok) {
            reportServiceRestored();
            const data: AuthResponse = await res.json();
            lastRefreshedOrgSlugRef.current = orgSlug?.toLowerCase() ?? null;
            setAccessToken(data.accessToken);
            setUser(data.user);
            applySessionLimits(data);
            return data.accessToken;
          }
          if (res && res.status < 500) {
            setAccessToken(null);
            setUser(null);
            return null;
          }
          if (Date.now() >= deadline) {
            setAccessToken(null);
            setUser(null);
            return null;
          }

          if (res && res.status === 503) {
            const problem = await parseResponseServiceProblem(res.clone());
            if (problem) {
              reportServiceProblem(problem, { method: 'POST' });
            } else {
              reportNetworkFailure({ status: res.status, method: 'POST' });
            }
          } else if (res && res.status >= 500) {
            reportNetworkFailure({ status: res.status, method: 'POST' });
          }

          setIsStarting(true);
          const retryAfter = Number(res?.headers.get('Retry-After'));
          await wait(Math.min(Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter : 5, 10) * 1000);
        }
      } finally {
        setIsStarting(false);
        refreshPromiseRef.current = null;
      }
    });

    return refreshPromiseRef.current;
  }, [applySessionLimits]);

  // Initialize session on mount via httpOnly refresh cookie
  useEffect(() => {
    let mounted = true;
    (async () => {
      try {
        await performRefresh();
      } catch {
        // user remains unauthenticated
      } finally {
        if (mounted) {
          setIsLoading(false);
        }
      }
    })();

    return () => {
      mounted = false;
    };
  }, [performRefresh]);

  // Request 6-digit OTP code
  const sendOtp = useCallback(async (email: string, turnstileToken?: string): Promise<OtpSendResponse> => {
    const headers: Record<string, string> = { 'Content-Type': 'application/json' };
    if (turnstileToken) {
      headers['cf-turnstile-response'] = turnstileToken;
    }
    const res = await fetchWith503Retry('/api/auth/send-otp', {
      method: 'POST',
      headers,
      body: JSON.stringify({
        email: email.trim(),
        ...(turnstileToken ? { 'cf-turnstile-response': turnstileToken } : {}),
      }),
    });

    const data = await res.json().catch(() => null);

    if (!res.ok) {
      if (res.status === 429) {
        throw new Error('Too many sign-in code requests. Please wait a few minutes before trying again.');
      }
      if (res.status === 503 || res.status === 504) {
        throw new Error(data?.detail || 'Service is starting up, please retry in a few seconds');
      }
      const errorMsg = data?.detail || data?.title || 'Unable to send verification code. Please try again.';
      throw new Error(errorMsg);
    }

    return {
      message: data.message || 'If an account exists, a code has been sent.',
      expiresInSeconds: data.expiresInSeconds || 300,
      devCode: data.devCode,
    };
  }, []);

  // Register a new user & request activation code
  const signUp = useCallback(async (payload: SignUpPayload): Promise<SignUpResponse> => {
    const turnstileToken = payload['cf-turnstile-response'];
    const headers: Record<string, string> = { 'Content-Type': 'application/json' };
    if (turnstileToken) {
      headers['cf-turnstile-response'] = turnstileToken;
    }
    const res = await fetch('/api/auth/signup', {
      method: 'POST',
      headers,
      body: JSON.stringify({
        email: payload.email.trim(),
        firstName: payload.firstName?.trim() || undefined,
        lastName: payload.lastName?.trim() || undefined,
        organizationName: payload.organizationName?.trim() || undefined,
        organizationSlug: payload.organizationSlug?.trim() || undefined,
        currency: payload.currency || 'USD',
        invitationToken: payload.invitationToken || undefined,
        ...(turnstileToken ? { 'cf-turnstile-response': turnstileToken } : {}),
      }),
    });

    const data = await res.json().catch(() => null);

    if (!res.ok) {
      if (res.status === 429) {
        throw new Error('Too many requests. Please wait a few minutes before trying again.');
      }
      if (res.status === 503 || res.status === 504) {
        throw new Error(data?.detail || 'Service is starting up, please retry in a few seconds');
      }
      const errorMsg = data?.detail || data?.title || 'Unable to complete registration. Please try again.';
      throw new Error(errorMsg);
    }

    return {
      success: true,
      message: data.message || 'Verification code sent to your email.',
      email: data.email || payload.email,
      orgSlug: data.orgSlug,
      expiresInSeconds: data.expiresInSeconds || 300,
      devCode: data.devCode,
    };
  }, []);

  // Verify OTP code & establish session (or return MFA challenge)
  const verifyOtp = useCallback(async (email: string, otp: string, turnstileToken?: string): Promise<VerifyOtpResult> => {
    const headers: Record<string, string> = { 'Content-Type': 'application/json' };
    if (turnstileToken) {
      headers['cf-turnstile-response'] = turnstileToken;
    }
    const res = await fetchWith503Retry('/api/auth/verify-otp', {
      method: 'POST',
      headers,
      body: JSON.stringify({
        email: email.trim(),
        otp: otp.trim(),
        ...(turnstileToken ? { 'cf-turnstile-response': turnstileToken } : {}),
      }),
      credentials: 'include',
    });

    const data = await res.json().catch(() => null);

    if (!res.ok) {
      if (res.status === 503 || res.status === 504) {
        throw new Error(data?.detail || 'Service is starting up, please retry in a few seconds');
      }
      const errorMsg = data?.detail || data?.title || 'Invalid or expired verification code.';
      throw new Error(errorMsg);
    }

    if (data?.status === 'mfa_required') {
      return { type: 'mfa_required', data: data as MfaRequiredResponse };
    }

    if (data?.status === 'mfa_enrollment_required') {
      return { type: 'mfa_enrollment_required', data: data as MfaEnrollmentRequiredResponse };
    }

    const authData = data as AuthResponse;
    lastRefreshedOrgSlugRef.current = null;
    tokenRef.current = authData.accessToken;
    setAccessToken(authData.accessToken);
    setUser(authData.user);
    applySessionLimits(authData);
    return { type: 'success', data: authData };
  }, [applySessionLimits]);

  const completeSession = useCallback((authData: AuthResponse) => {
    lastRefreshedOrgSlugRef.current = null;
    tokenRef.current = authData.accessToken;
    setAccessToken(authData.accessToken);
    setUser(authData.user);
    applySessionLimits(authData);
  }, [applySessionLimits]);

  const completeStepUp = useCallback((newToken: string) => {
    lastRefreshedOrgSlugRef.current = null;
    tokenRef.current = newToken;
    setAccessToken(newToken);
  }, []);

  // Logout session
  const clearSession = useCallback(() => {
    lastRefreshedOrgSlugRef.current = null;
    tokenRef.current = null;
    setAccessToken(null);
    setUser(null);
  }, []);

  const logout = useCallback(async (reason?: SignOutReason): Promise<void> => {
    try {
      const currentToken = tokenRef.current;
      // Works without a live access token: the server revokes the sign-in from the refresh cookie.
      await fetch(reason ? `/api/auth/logout?reason=${reason}` : '/api/auth/logout', {
        method: 'POST',
        headers: currentToken ? { Authorization: `Bearer ${currentToken}` } : {},
        credentials: 'include',
      });
    } catch {
      // ignore network errors on logout
    } finally {
      clearSession();
    }
  }, [clearSession]);

  // Authenticated fetch wrapper with automatic Bearer injection and single-flight silent refresh on 401
  const authFetch = useCallback(
    async (url: string, init?: RequestInit, planDialog?: boolean): Promise<Response> => {
      let token = tokenRef.current;

      const headers = new Headers(init?.headers || {});
      if (token && !headers.has('Authorization')) {
        headers.set('Authorization', `Bearer ${token}`);
      }
      const requestPath = new URL(url.toString(), window.location.origin).pathname;
      const isTenantFreeRequest =
        requestPath.startsWith('/api/auth/') ||
        requestPath === '/api/organizations' ||
        requestPath.startsWith('/api/invitations/');
      const orgSlug = isTenantFreeRequest ? null : getOrgSlugFromLocation();
      if (orgSlug && !headers.has(ORG_HEADER)) {
        headers.set(ORG_HEADER, orgSlug);
      }

      const method = (init?.method || 'GET').toUpperCase();
      const isNonGet = method !== 'GET' && method !== 'HEAD';
      const isAuthRoute = requestPath.startsWith('/api/auth/');
      const userWrite = isNonGet && !isAuthRoute;

      const effectivePlanDialog = planDialog !== undefined
        ? planDialog
        : (init as { planDialog?: boolean } | undefined)?.planDialog;

      const shouldAnnounce = effectivePlanDialog !== undefined
        ? effectivePlanDialog
        : isNonGet;

      const { planDialog: _, ...fetchInit } = (init as { planDialog?: boolean } & RequestInit) || {};

      let res: Response;
      try {
        res = await fetchWith503Retry(
          url,
          { ...fetchInit, headers, credentials: 'include' },
          3,
          { userWrite }
        );
      } catch (networkErr) {
        if (requestPath.startsWith('/api/')) {
          reportNetworkFailure({ method, userWrite });
        }
        throw networkErr;
      }

      if (requestPath.startsWith('/api/')) {
        if (res.status === 502 || res.status === 504) {
          reportNetworkFailure({ status: res.status, method, userWrite });
        } else if (res.ok) {
          reportServiceRestored();
        }
      }

      // Plan denials (limits, modules, plans) get one shared explanation; callers still handle the response.
      if (shouldAnnounce) {
        void announceEntitlementDenial(res);
      }
      // Hook adopts /api/entitlements versions itself — observing here would duplicate the reload.
      if (requestPath !== '/api/entitlements') {
        observeEntitlementsVersionHeader(
          orgSlug,
          getOrgSlugFromLocation(),
          res.headers.get(ENTITLEMENTS_VERSION_HEADER),
        );
      }

      // Handle 401 with silent refresh & request retry
      if (res.status === 401) {
        const newToken = await performRefresh();
        if (newToken) {
          const retryHeaders = new Headers(init?.headers || {});
          retryHeaders.set('Authorization', `Bearer ${newToken}`);
          if (orgSlug && !retryHeaders.has(ORG_HEADER)) {
            retryHeaders.set(ORG_HEADER, orgSlug);
          }
          try {
            res = await fetchWith503Retry(
              url,
              { ...fetchInit, headers: retryHeaders, credentials: 'include' },
              3,
              { userWrite }
            );
          } catch (retryNetworkErr) {
            if (requestPath.startsWith('/api/')) {
              reportNetworkFailure({ method, userWrite });
            }
            throw retryNetworkErr;
          }

          if (requestPath.startsWith('/api/')) {
            if (res.status === 502 || res.status === 504) {
              reportNetworkFailure({ status: res.status, method, userWrite });
            } else if (res.ok) {
              reportServiceRestored();
            }
          }

          if (shouldAnnounce) {
            void announceEntitlementDenial(res);
          }
          if (requestPath !== '/api/entitlements') {
            observeEntitlementsVersionHeader(
              orgSlug,
              getOrgSlugFromLocation(),
              res.headers.get(ENTITLEMENTS_VERSION_HEADER),
            );
          }
        }
      }

      return res;
    },
    [performRefresh]
  );

  const refreshUser = useCallback(async () => {
    try {
      const res = await authFetch('/api/auth/me');
      if (res.ok) {
        const data: UserDto = await res.json();
        setUser(data);
      }
    } catch {}
  }, [authFetch]);

  const isAuthenticated = !!user && !!accessToken;
  const hasIdleLimit = idleMs !== null;

  useEffect(() => {
    if (!isAuthenticated || !hasIdleLimit || idleMsRef.current === null) return;
    const session = startIdleSession({
      idleMs: idleMsRef.current,
      sessionExpiresAt,
      refreshedAt: refreshedAtRef.current,
      onWarning: setSessionWarning,
      onRefreshDue: () => {
        void performRefresh();
      },
      onRemoteIdleMs: (ms, at) => {
        refreshedAtRef.current = Math.max(refreshedAtRef.current, at);
        setIdleMs(ms);
      },
      onSignOut: (reason, origin) => {
        setSignedOutNoticeText(signedOutNotice(reason, idleMsRef.current ?? 0));
        if (origin) void logout(reason);
        else clearSession();
      },
    });
    idleSessionRef.current = session;
    return () => {
      session.stop();
      idleSessionRef.current = null;
      setSessionWarning(null);
    };
  }, [isAuthenticated, hasIdleLimit, sessionExpiresAt, performRefresh, logout, clearSession]);

  const value: AuthContextType = {
    user,
    accessToken,
    isAuthenticated,
    isLoading,
    isStarting,
    sendOtp,
    signUp,
    verifyOtp,
    completeSession,
    completeStepUp,
    logout,
    clearSession,
    authFetch,
    refreshUser,
    refreshSession: performRefresh,
    signedOutNotice: signedOutNoticeText,
  };

  return (
    <AuthContext.Provider value={value}>
      {children}
      {sessionWarning && (
        <SessionTimeoutDialog
          warning={sessionWarning}
          onStay={() => idleSessionRef.current?.markActive()}
          onDismissExpiry={() => idleSessionRef.current?.dismissExpiryWarning()}
          onSignOut={() => {
            idleSessionRef.current?.stop();
            void logout();
          }}
        />
      )}
    </AuthContext.Provider>
  );
};

export const useAuth = (): AuthContextType => {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
};
