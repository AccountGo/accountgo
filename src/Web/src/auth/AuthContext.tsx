import React, { createContext, useContext, useState, useEffect, useRef, useCallback } from 'react';
import { UserDto, AuthResponse, OtpSendResponse, SignUpPayload, SignUpResponse, AuthContextType } from '../types/auth';

const AuthContext = createContext<AuthContextType | undefined>(undefined);

async function fetchWith503Retry(
  input: RequestInfo | URL,
  init?: RequestInit,
  maxRetries = 3
): Promise<Response> {
  let attempt = 0;
  while (true) {
    const res = await fetch(input, init);
    if (res.status === 503 && attempt < maxRetries) {
      attempt++;
      const retryAfterHeader = res.headers.get('Retry-After');
      let delaySec = retryAfterHeader ? parseInt(retryAfterHeader, 10) : 5;
      if (isNaN(delaySec) || delaySec <= 0) delaySec = 5;
      delaySec = Math.min(delaySec, 10);
      await new Promise((resolve) => setTimeout(resolve, delaySec * 1000));
      continue;
    }
    return res;
  }
}

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [user, setUser] = useState<UserDto | null>(null);
  const [accessToken, setAccessToken] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState<boolean>(true);

  // Ref to hold current token synchronously for fetch interceptor & refresh loops
  const tokenRef = useRef<string | null>(null);
  tokenRef.current = accessToken;

  // Single-flight refresh promise to prevent multiple concurrent refresh calls
  const refreshPromiseRef = useRef<Promise<string | null> | null>(null);

  // Silent refresh helper
  const performRefresh = useCallback(async (): Promise<string | null> => {
    if (refreshPromiseRef.current) {
      return refreshPromiseRef.current;
    }

    refreshPromiseRef.current = (async () => {
      try {
        const res = await fetch('/api/auth/refresh', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          credentials: 'include',
        });

        if (!res.ok) {
          setAccessToken(null);
          setUser(null);
          return null;
        }

        const data: AuthResponse = await res.json();
        setAccessToken(data.accessToken);
        setUser(data.user);
        return data.accessToken;
      } catch (err) {
        setAccessToken(null);
        setUser(null);
        return null;
      } finally {
        refreshPromiseRef.current = null;
      }
    })();

    return refreshPromiseRef.current;
  }, []);

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
  const sendOtp = useCallback(async (email: string): Promise<OtpSendResponse> => {
    const res = await fetchWith503Retry('/api/auth/send-otp', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email: email.trim() }),
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
    const res = await fetch('/api/auth/signup', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        email: payload.email.trim(),
        firstName: payload.firstName.trim(),
        lastName: payload.lastName.trim(),
        organizationName: payload.organizationName?.trim() || undefined,
        organizationSlug: payload.organizationSlug?.trim() || undefined,
        currency: payload.currency || 'PHP',
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

  // Verify OTP code & establish session
  const verifyOtp = useCallback(async (email: string, otp: string): Promise<void> => {
    const res = await fetchWith503Retry('/api/auth/verify-otp', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        email: email.trim(),
        otp: otp.trim(),
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

    const authData = data as AuthResponse;
    setAccessToken(authData.accessToken);
    setUser(authData.user);
  }, []);

  // Logout session
  const logout = useCallback(async (): Promise<void> => {
    try {
      const currentToken = tokenRef.current;
      await fetch('/api/auth/logout', {
        method: 'POST',
        headers: currentToken ? { Authorization: `Bearer ${currentToken}` } : {},
        credentials: 'include',
      });
    } catch {
      // ignore network errors on logout
    } finally {
      setAccessToken(null);
      setUser(null);
    }
  }, []);

  // Authenticated fetch wrapper with automatic Bearer injection and single-flight silent refresh on 401
  const authFetch = useCallback(
    async (url: string, init?: RequestInit): Promise<Response> => {
      let token = tokenRef.current;

      const headers = new Headers(init?.headers || {});
      if (token && !headers.has('Authorization')) {
        headers.set('Authorization', `Bearer ${token}`);
      }

      const method = (init?.method || 'GET').toUpperCase();
      const shouldRetry503 = method === 'GET';

      let res = shouldRetry503
        ? await fetchWith503Retry(url, { ...init, headers, credentials: 'include' })
        : await fetch(url, { ...init, headers, credentials: 'include' });

      // Handle 401 with silent refresh & request retry
      if (res.status === 401) {
        const newToken = await performRefresh();
        if (newToken) {
          const retryHeaders = new Headers(init?.headers || {});
          retryHeaders.set('Authorization', `Bearer ${newToken}`);
          res = shouldRetry503
            ? await fetchWith503Retry(url, { ...init, headers: retryHeaders, credentials: 'include' })
            : await fetch(url, { ...init, headers: retryHeaders, credentials: 'include' });
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

  const value: AuthContextType = {
    user,
    accessToken,
    isAuthenticated: !!user && !!accessToken,
    isLoading,
    sendOtp,
    signUp,
    verifyOtp,
    logout,
    authFetch,
    refreshUser,
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
};

export const useAuth = (): AuthContextType => {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
};
