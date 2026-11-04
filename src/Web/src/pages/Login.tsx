import React, { useState, useEffect, useRef } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { getPersistedOrgSlug, setPersistedOrgSlug } from '../lib/storage';
import { resolvePostAuthDestination } from './loginContinuation';
import { clearInvitationToken, getInvitationToken, storeInvitationToken } from '../lib/invitationStorage';
import { GoogleSignInButton } from '../components/GoogleSignInButton';
import { MfaChallenge } from '../components/mfa/MfaChallenge';
import { MfaEnrollWizard } from '../components/mfa/MfaEnrollWizard';
import { TurnstileWidget, TurnstileWidgetHandle } from '../components/TurnstileWidget';
import { MfaRequiredResponse, MfaEnrollmentRequiredResponse } from '../types/auth';
import {
  Mail,
  ArrowRight,
  ArrowLeft,
  Loader2,
  AlertCircle,
  Clock,
  Terminal,
  Sparkles,
} from 'lucide-react';

const PRIVACY_POLICY_URL = 'https://www.accountgo.dev/legal/privacy-policy/';
const TERMS_OF_USE_URL = 'https://www.accountgo.dev/legal/terms-of-use/';

interface LoginProps {
  initialMode?: 'signin' | 'signup';
}

export const Login: React.FC<LoginProps> = ({ initialMode }) => {
  const navigate = useNavigate();
  const location = useLocation();
  const { isAuthenticated, isLoading: isAuthLoading, sendOtp, signUp, verifyOtp, completeSession, authFetch } = useAuth();
  const queryInviteToken = new URLSearchParams(location.search).get('invite');
  const [invitationToken, setInvitationToken] = useState<string | null>(
    () => queryInviteToken || getInvitationToken()
  );

  // Mode state: 'signin' or 'signup'
  const [mode, setMode] = useState<'signin' | 'signup'>(() => {
    if (initialMode) return initialMode;
    if (location.pathname === '/signup') return 'signup';
    const params = new URLSearchParams(location.search);
    if (params.get('mode') === 'signup') return 'signup';
    return 'signin';
  });

  // Step state: 1 = Credentials request, 2 = 6-digit OTP verification, 'mfa_challenge' = Factor B challenge, 'mfa_enroll' = Factor B enrollment
  const [step, setStep] = useState<1 | 2 | 'mfa_challenge' | 'mfa_enroll'>(1);
  const [mfaChallengeData, setMfaChallengeData] = useState<MfaRequiredResponse | null>(null);
  const [mfaEnrollData, setMfaEnrollData] = useState<MfaEnrollmentRequiredResponse | null>(null);

  // Form fields
  const [email, setEmail] = useState<string>(() => (import.meta.env.DEV ? 'admin@accountgo.ph' : ''));
  const [targetOrgSlug, setTargetOrgSlug] = useState<string | null>(null);
  const [acceptedTerms, setAcceptedTerms] = useState<boolean>(false);

  // OTP inputs
  const [otp, setOtp] = useState<string[]>(['', '', '', '', '', '']);
  const [devCode, setDevCode] = useState<string | null>(null);
  const [isSending, setIsSending] = useState<boolean>(false);
  const [isVerifying, setIsVerifying] = useState<boolean>(false);
  const [googleProgress, setGoogleProgress] = useState<'selecting' | 'signing-in' | null>(null);
  const googleAttempt = useRef(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  // Turnstile verification tokens and widget refs
  const [turnstileToken, setTurnstileToken] = useState<string>('');
  const [verifyTurnstileToken, setVerifyTurnstileToken] = useState<string>('');
  const signInTurnstileRef = useRef<TurnstileWidgetHandle>(null);
  const signUpTurnstileRef = useRef<TurnstileWidgetHandle>(null);
  const verifyTurnstileRef = useRef<TurnstileWidgetHandle>(null);

  // Timers
  const [resendCooldown, setResendCooldown] = useState<number>(0);
  const [expirySeconds, setExpirySeconds] = useState<number>(300);

  // Input refs for the 6 OTP boxes
  const inputRefs = useRef<(HTMLInputElement | null)[]>([]);
  const verificationOwnsNavigation = useRef(false);
  const googleMfa = useRef(false);

  // If already authenticated, redirect away
  useEffect(() => {
    if (queryInviteToken) {
      storeInvitationToken(queryInviteToken);
      setInvitationToken(queryInviteToken);
      navigate(location.pathname, { replace: true, state: location.state });
      return;
    }
    if (!isAuthLoading && isAuthenticated && !isVerifying && !verificationOwnsNavigation.current) {
      if (invitationToken) {
        navigate(`/invite/${encodeURIComponent(invitationToken)}`, { replace: true });
        return;
      }
      // Resolve from trusted server state through the SAME resolver as post-verification, so an
      // already-authenticated visit (reload / session restore) with an uninitialized signup workspace is
      // routed into guided continuation rather than straight to an unusable dashboard.
      let cancelled = false;
      void (async () => {
        const decision = await resolvePostAuthDestination(authFetch, {
          targetOrgSlug: null,
          persistedSlug: getPersistedOrgSlug(),
          fromPath: (() => {
            const from = (location.state as any)?.from;
            if (!from?.pathname) return undefined;
            return `${from.pathname}${from.search || ''}`;
          })(),
        });
        if (cancelled) return;
        switch (decision.kind) {
          case 'continue':
            setPersistedOrgSlug(decision.slug);
            navigate(`/create-org?continue=${encodeURIComponent(decision.slug)}`, { replace: true });
            return;
          case 'dashboard':
            setPersistedOrgSlug(decision.slug);
            navigate(`/org/${decision.slug}/dashboard`, { replace: true });
            return;
          case 'path':
            navigate(decision.path, { replace: true });
            return;
          case 'select-org':
          default:
            navigate('/select-org', { replace: true, state: { fromSignIn: true } });
            return;
        }
      })();
      return () => {
        cancelled = true;
      };
    }
  }, [isAuthenticated, isAuthLoading, navigate, authFetch, location.pathname, location.search, location.state, queryInviteToken, invitationToken, isVerifying]);

  // Handle countdowns for resend cooldown and code expiry
  useEffect(() => {
    if (step !== 2) return;

    const timer = setInterval(() => {
      setResendCooldown((prev) => (prev > 0 ? prev - 1 : 0));
      setExpirySeconds((prev) => (prev > 0 ? prev - 1 : 0));
    }, 1000);

    return () => clearInterval(timer);
  }, [step]);

  // Focus the first OTP box when transitioning to Step 2
  useEffect(() => {
    if (step === 2) {
      setTimeout(() => {
        inputRefs.current[0]?.focus();
      }, 100);
    }
  }, [step]);

  // Format seconds to mm:ss
  const formatTime = (seconds: number) => {
    const mins = Math.floor(seconds / 60);
    const secs = seconds % 60;
    return `${mins}:${secs.toString().padStart(2, '0')}`;
  };

  const goToOtpEntry = (opts?: { expiresInSeconds?: number; notice?: string | null; clearDigits?: boolean }) => {
    setStep(2);
    setResendCooldown(30);
    setExpirySeconds(opts?.expiresInSeconds ?? 300);
    setDevCode(null);
    if (opts?.clearDigits !== false) {
      setOtp(['', '', '', '', '', '']);
    }
    setErrorMessage(opts?.notice ?? null);
  };

  // Submit Step 1 (Sign In): Send OTP code
  const handleSendCode = async (e?: React.FormEvent) => {
    if (e) e.preventDefault();
    if (!email || !email.includes('@')) {
      setErrorMessage('Please enter a valid work email address.');
      return;
    }
    if (!acceptedTerms) {
      setErrorMessage('Please accept the Privacy Policy and Terms of Use to continue.');
      return;
    }

    setErrorMessage(null);
    setIsSending(true);

    try {
      const res = await sendOtp(email, turnstileToken);
      goToOtpEntry({ expiresInSeconds: res.expiresInSeconds || 300, notice: null });
      if (res.devCode) {
        setDevCode(res.devCode);
        setOtp(res.devCode.split(''));
      }
    } catch (err: any) {
      signInTurnstileRef.current?.reset();
      setTurnstileToken('');
      const message = String(err?.message || 'Failed to send verification code.');
      // Rate-limit / cooldown: a code may already be in the inbox — still open the entry step.
      const alreadyHaveCode =
        /too many|wait .* (second|minute)|already (been )?sent|try again/i.test(message);
      if (alreadyHaveCode) {
        goToOtpEntry({
          expiresInSeconds: 300,
          notice: `${message} If you already received a code, enter it below.`,
        });
      } else {
        setErrorMessage(message);
      }
    } finally {
      setIsSending(false);
    }
  };

  const handleEnterExistingCode = () => {
    if (!email || !email.includes('@')) {
      setErrorMessage('Enter your work email first, then open the code entry step.');
      return;
    }
    goToOtpEntry({
      expiresInSeconds: 300,
      notice: 'Enter the 6-digit code from your email. Request a new code only if this one has expired.',
    });
  };

  // Submit Step 1 (Sign Up): Register account & Send Welcome OTP
  const handleSignUp = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!email || !email.includes('@')) {
      setErrorMessage('Please enter a valid work email address.');
      return;
    }
    if (!acceptedTerms) {
      setErrorMessage('Please accept the Privacy Policy and Terms of Use to continue.');
      return;
    }

    setErrorMessage(null);
    setIsSending(true);

    try {
      const res = await signUp({
        email: email.trim(),
        invitationToken: invitationToken || undefined,
        'cf-turnstile-response': turnstileToken,
      });

      if (res.orgSlug) {
        setTargetOrgSlug(res.orgSlug);
      }

      setStep(2);
      setResendCooldown(30);
      setExpirySeconds(res.expiresInSeconds || 300);
      if (res.devCode) {
        setDevCode(res.devCode);
        setOtp(res.devCode.split(''));
      } else {
        setDevCode(null);
        setOtp(['', '', '', '', '', '']);
      }
    } catch (err: any) {
      signUpTurnstileRef.current?.reset();
      setTurnstileToken('');
      setErrorMessage(err.message || 'Registration failed. Please try again.');
    } finally {
      setIsSending(false);
    }
  };

  // Common post-authentication routing (invitation acceptance or dashboard redirection)
  const handlePostAuthSuccess = async (targetSlugOverride?: string | null) => {
    const pendingToken = invitationToken || getInvitationToken();
    if (pendingToken) {
      const acceptResponse = await authFetch(`/api/invitations/${encodeURIComponent(pendingToken)}/accept`, {
        method: 'POST',
      });
      if (acceptResponse.status === 403) {
        throw new Error('This invitation was sent to a different email. Use the invited email address and try again.');
      }
      if (acceptResponse.status === 410) {
        clearInvitationToken();
        setInvitationToken(null);
        throw new Error('This invitation has expired or has already been accepted.');
      }
      if (acceptResponse.status === 404) {
        clearInvitationToken();
        setInvitationToken(null);
        throw new Error('This invitation is invalid or has been revoked.');
      }
      if (!acceptResponse.ok) {
        throw new Error('We could not accept this invitation. You can try again from Pending invitations.');
      }
      const accepted = (await acceptResponse.json()) as { slug: string };
      clearInvitationToken();
      setInvitationToken(null);
      setPersistedOrgSlug(accepted.slug);
      navigate(`/org/${encodeURIComponent(accepted.slug)}/dashboard`, { replace: true });
      return;
    }

    // Successfully authenticated! Resolve the post-auth destination from TRUSTED SERVER STATE so a
    // returning user with an uninitialized signup workspace is routed into guided continuation across
    // every entry path (explicit slug, persisted slug, original destination, or sole workspace), not just
    // when the in-memory signup slug survives.
    const fromLoc = (location.state as any)?.from;
    const fromPath = fromLoc?.pathname
      ? `${fromLoc.pathname}${fromLoc.search || ''}`
      : undefined;
    const decision = await resolvePostAuthDestination(authFetch, {
      targetSlugOverride,
      targetOrgSlug,
      persistedSlug: getPersistedOrgSlug(),
      fromPath,
    });

    switch (decision.kind) {
      case 'continue':
        setPersistedOrgSlug(decision.slug);
        navigate(`/create-org?continue=${encodeURIComponent(decision.slug)}`, { replace: true });
        return;
      case 'dashboard':
        setPersistedOrgSlug(decision.slug);
        navigate(`/org/${decision.slug}/dashboard`, { replace: true });
        return;
      case 'path':
        navigate(decision.path, { replace: true });
        return;
      case 'select-org':
      default:
        navigate('/select-org', { replace: true, state: { fromSignIn: true } });
        return;
    }
  };

  // Submit Step 2: Verify OTP code
  const handleVerifyCode = async (codeToVerify?: string) => {
    const code = codeToVerify || otp.join('');
    if (code.length !== 6 || isVerifying || verificationOwnsNavigation.current) return;

    setErrorMessage(null);
    setIsVerifying(true);
    verificationOwnsNavigation.current = true;
    let verified = false;

    try {
      const result = await verifyOtp(email, code, verifyTurnstileToken);

      googleMfa.current = false;
      if (result.type === 'mfa_required') {
        setMfaChallengeData(result.data);
        setStep('mfa_challenge');
        verificationOwnsNavigation.current = false;
        return;
      }

      if (result.type === 'mfa_enrollment_required') {
        setMfaEnrollData(result.data);
        setStep('mfa_enroll');
        verificationOwnsNavigation.current = false;
        return;
      }

      verified = true;
      await handlePostAuthSuccess();
    } catch (err: any) {
      verifyTurnstileRef.current?.reset();
      setVerifyTurnstileToken('');
      setErrorMessage(err.message || 'Invalid or expired verification code.');
    } finally {
      // After verification, keep ownership so auth redirects cannot replace acceptance errors or navigation.
      if (!verified) verificationOwnsNavigation.current = false;
      setIsVerifying(false);
    }
  };

  // Handle keystrokes on individual OTP inputs
  const handleOtpChange = (index: number, val: string) => {
    const numericVal = val.replace(/\D/g, '');
    if (!numericVal && val !== '') return;

    const newOtp = [...otp];
    newOtp[index] = numericVal.slice(-1);
    setOtp(newOtp);

    // Auto-advance to next input
    if (numericVal && index < 5) {
      inputRefs.current[index + 1]?.focus();
    }

    // Auto-submit when all 6 digits are populated
    if (numericVal && index === 5) {
      const fullCode = newOtp.join('');
      if (fullCode.length === 6) {
        handleVerifyCode(fullCode);
      }
    }
  };

  // Backspace navigation across OTP boxes
  const handleKeyDown = (index: number, e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Backspace' && !otp[index] && index > 0) {
      inputRefs.current[index - 1]?.focus();
    }
  };

  // Handle pasting full 6-digit code into OTP boxes
  const handlePaste = (e: React.ClipboardEvent) => {
    e.preventDefault();
    const pasteData = e.clipboardData.getData('text').trim().replace(/\D/g, '');
    if (pasteData.length >= 6) {
      const digits = pasteData.slice(0, 6).split('');
      setOtp(digits);
      inputRefs.current[5]?.focus();
      handleVerifyCode(digits.join(''));
    }
  };

  const isOtpComplete = otp.every((d) => d.length === 1);

  return (
    <div className="min-h-screen bg-[var(--bg-canvas)] flex items-center justify-center p-4 sm:p-6 transition-colors duration-200">
      {googleProgress && <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 p-6" role="status" aria-live="polite" aria-busy="true">
        <div className="rounded-2xl bg-[var(--bg-surface)] p-8 text-center shadow-2xl">
          <Loader2 aria-hidden="true" className="mx-auto mb-4 h-8 w-8 animate-spin text-[var(--brand-primary)]" />
          <p className="font-semibold text-[var(--text-main)]">{googleProgress === 'selecting' ? 'Continue with Google' : 'Signing you in…'}</p>
          {googleProgress === 'selecting' && <button type="button" className="mt-4 underline text-sm text-[var(--text-secondary)]" onClick={() => { googleAttempt.current = false; setGoogleProgress(null); }}>Cancel</button>}
        </div>
      </div>}
      <div inert={!!googleProgress} className="w-full max-w-md bg-[var(--bg-surface)] backdrop-blur-2xl rounded-3xl p-6 sm:p-8 shadow-2xl shadow-black/80 ring-1 ring-white/10 relative overflow-hidden">
        {/* Step 1: Sign In or Sign Up Form */}
        {step === 1 && (
          <div className="space-y-6 animate-fade-in">
            <div className="flex justify-center">
              <div className="w-14 h-14 rounded-2xl bg-[var(--brand-primary)]/15 ring-1 ring-[var(--brand-ring)]/40 flex items-center justify-center">
                <img src="/accountgo-mark.svg" alt="AccountGo" className="w-9 h-9 rounded-xl" />
              </div>
            </div>

            {typeof location.state?.notice === 'string' && (
              <p role="status" className="p-3 rounded-xl bg-amber-500/10 text-amber-300 text-sm">
                {location.state.notice}
              </p>
            )}


            {/* Heading & Subtitle */}
            <div className="space-y-2 text-center">
              <h1 className="text-2xl sm:text-3xl font-bold tracking-tight text-[var(--text-main)]">
                {mode === 'signin' ? (
                  <>
                    Sign In to Account<span className="text-[var(--brand-primary)]">Go</span>
                  </>
                ) : (
                  <>
                    Join Account<span className="text-[var(--brand-primary)]">Go</span>
                  </>
                )}
              </h1>
              <p className="text-sm text-[var(--text-secondary)]">
                {mode === 'signin' ? "Don't have an account? " : 'Already have an account? '}
                <button type="button" className="font-semibold text-[var(--text-main)] underline underline-offset-2" onClick={() => {
                  setMode(mode === 'signin' ? 'signup' : 'signin');
                  setErrorMessage(null);
                  if (email === 'admin@accountgo.ph') setEmail('');
                }}>{mode === 'signin' ? 'Sign up' : 'Sign in'}</button>
              </p>
              {invitationToken && (
                <p className="text-sm text-[var(--brand-primary)]">
                  You are joining a workspace. Use the email address that received the invitation; no new workspace will be created.
                </p>
              )}
            </div>

            {/* Error Banner */}
            {errorMessage && (
              <div
                role="alert"
                aria-live="assertive"
                className="flex items-start gap-2.5 p-3 rounded-xl bg-rose-500/10 text-rose-400 text-xs font-medium ring-1 ring-rose-500/20 animate-fade-in"
              >
                <AlertCircle className="w-4 h-4 flex-shrink-0 mt-0.5" />
                <span className="flex-1">{errorMessage}</span>
              </div>
            )}

            {import.meta.env.VITE_GOOGLE_CLIENT_ID && (
              <GoogleSignInButton clientId={import.meta.env.VITE_GOOGLE_CLIENT_ID} enabled={acceptedTerms} onError={setErrorMessage}
                onStart={() => { googleAttempt.current = true; setGoogleProgress('selecting'); }}
                onCredential={async (credential) => {
                  if (!googleAttempt.current) return;
                  googleAttempt.current = false;
                  setGoogleProgress('signing-in');
                  setErrorMessage(null);
                  verificationOwnsNavigation.current = true;
                  try {
                    const res = await fetch('/api/auth/google', { method: 'POST', credentials: 'include',
                      headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ idToken: credential }) });
                    const data = await res.json();
                    if (!res.ok) throw new Error(data.detail || 'Google sign-in failed.');
                    if (data.status === 'mfa_required') { googleMfa.current = true; setMfaChallengeData(data); setStep('mfa_challenge'); verificationOwnsNavigation.current = false; return; }
                    if (data.status === 'mfa_enrollment_required') { googleMfa.current = true; setMfaEnrollData(data); setStep('mfa_enroll'); verificationOwnsNavigation.current = false; return; }
                    completeSession(data);
                    await handlePostAuthSuccess();
                  } catch (error) {
                    verificationOwnsNavigation.current = false;
                    setErrorMessage(error instanceof Error ? error.message : 'Google sign-in failed.');
                  } finally {
                    setGoogleProgress(null);
                  }
                }} />
            )}

            {import.meta.env.VITE_GOOGLE_CLIENT_ID && (
              <div className="flex items-center gap-4 text-sm text-[var(--text-muted)]" aria-hidden="true">
                <span className="h-px flex-1 bg-[var(--border-subtle)]" />or<span className="h-px flex-1 bg-[var(--border-subtle)]" />
              </div>
            )}

            {/* Form Mode: SIGN IN */}
            {mode === 'signin' && (
              <form onSubmit={handleSendCode} className="space-y-4">
                <div className="space-y-1.5">
                  <label
                    htmlFor="email-input"
                    className="text-xs font-semibold text-[var(--text-secondary)] uppercase tracking-wider"
                  >
                    Work Email
                  </label>
                  <div className="relative">
                    <Mail className="w-4 h-4 text-[var(--text-muted)] absolute left-3.5 top-1/2 -translate-y-1/2 pointer-events-none" />
                    <input
                      id="email-input"
                      type="email"
                      required
                      value={email}
                      onChange={(e) => setEmail(e.target.value)}
                      placeholder="you@company.com"
                      autoFocus
                      autoComplete="email"
                      className="w-full pl-10 pr-4 py-3 rounded-xl bg-[var(--bg-canvas)] text-sm font-medium text-[var(--text-main)] placeholder-[var(--text-muted)] ring-1 ring-white/10 focus:ring-2 focus:ring-[var(--brand-primary)] focus:outline-none transition-all shadow-inner"
                    />
                  </div>
                </div>

                {/* Dev Quick Test Credential Chip */}
                {import.meta.env.DEV && (
                  <div className="flex items-center justify-between text-xs text-[var(--text-muted)] pt-1">
                    <span>Default Admin:</span>
                    <button
                      type="button"
                      onClick={() => setEmail('admin@accountgo.ph')}
                      className="font-mono text-[11px] text-[var(--brand-primary)] hover:underline cursor-pointer"
                    >
                      admin@accountgo.ph
                    </button>
                  </div>
                )}

                {/* Turnstile Bot Protection */}
                <div className="flex justify-center pt-1">
                  <TurnstileWidget
                    ref={signInTurnstileRef}
                    action="login"
                    onVerify={(token) => setTurnstileToken(token)}
                    onExpire={() => setTurnstileToken('')}
                  />
                </div>

                {/* Privacy and Terms Acceptance Checkbox */}
                <label
                  htmlFor="signin-accept-terms"
                  className="flex items-start gap-2.5 text-xs text-[var(--text-secondary)] pt-1 cursor-pointer select-none"
                >
                  <input
                    id="signin-accept-terms"
                    type="checkbox"
                    required
                    checked={acceptedTerms}
                    onChange={(e) => setAcceptedTerms(e.target.checked)}
                    className="mt-0.5 rounded bg-[var(--bg-canvas)] border-white/20 text-[var(--brand-primary)] focus:ring-[var(--brand-primary)] focus:ring-offset-0 accent-[var(--brand-primary)] cursor-pointer"
                  />
                  <span className="leading-relaxed">
                    By signing in, you accept the <a href={PRIVACY_POLICY_URL} target="_blank" rel="noopener noreferrer" className="underline underline-offset-2 hover:text-[var(--text-primary)]">Privacy Policy</a> and <a href={TERMS_OF_USE_URL} target="_blank" rel="noopener noreferrer" className="underline underline-offset-2 hover:text-[var(--text-primary)]">Terms of Use</a>.
                  </span>
                </label>

                {/* Submit Button */}
                <button
                  type="submit"
                  disabled={isSending || !email || !acceptedTerms}
                  aria-busy={isSending}
                  className="w-full py-3.5 px-4 rounded-xl bg-[var(--brand-primary)] text-[var(--brand-text)] font-semibold text-sm flex items-center justify-center gap-2 shadow-lg shadow-[var(--brand-primary)]/25 hover:brightness-110 active:scale-[0.99] disabled:opacity-50 disabled:pointer-events-none transition-all cursor-pointer"
                >
                  {isSending ? (
                    <>
                      <Loader2 className="w-4 h-4 animate-spin" />
                      <span>Sending Code...</span>
                    </>
                  ) : (
                    <>
                      <span>Send Verification Code</span>
                      <ArrowRight className="w-4 h-4" />
                    </>
                  )}
                </button>

                <div className="flex flex-col items-center gap-2 pt-2">
                  <button
                    type="button"
                    onClick={handleEnterExistingCode}
                    className="text-xs text-[var(--text-secondary)] hover:text-[var(--brand-primary)] transition-colors cursor-pointer"
                  >
                    Already have a code? <span className="font-semibold text-[var(--brand-primary)] underline">Enter it here</span>
                  </button>
                </div>
              </form>
            )}

            {/* Form Mode: SIGN UP */}
            {mode === 'signup' && (
              <form onSubmit={handleSignUp} className="space-y-4">
                {/* Email Address */}
                <div className="space-y-1.5">
                  <label
                    htmlFor="signup-email"
                    className="text-xs font-semibold text-[var(--text-secondary)] uppercase tracking-wider"
                  >
                    Work Email
                  </label>
                  <div className="relative">
                    <Mail className="w-4 h-4 text-[var(--text-muted)] absolute left-3.5 top-1/2 -translate-y-1/2 pointer-events-none" />
                    <input
                      id="signup-email"
                      type="email"
                      required
                      value={email}
                      onChange={(e) => setEmail(e.target.value)}
                      placeholder="elena@pacificrim.com"
                      autoComplete="email"
                      className="w-full pl-10 pr-4 py-2.5 rounded-xl bg-[var(--bg-canvas)] text-sm font-medium text-[var(--text-main)] placeholder-[var(--text-muted)] ring-1 ring-white/10 focus:ring-2 focus:ring-[var(--brand-primary)] focus:outline-none transition-all shadow-inner"
                    />
                  </div>
                </div>

                {/* Turnstile Bot Protection */}
                <div className="flex justify-center pt-1">
                  <TurnstileWidget
                    ref={signUpTurnstileRef}
                    action="signup"
                    onVerify={(token) => setTurnstileToken(token)}
                    onExpire={() => setTurnstileToken('')}
                  />
                </div>

                {/* Privacy and Terms Acceptance Checkbox */}
                <label
                  htmlFor="signup-accept-terms"
                  className="flex items-start gap-2.5 text-xs text-[var(--text-secondary)] pt-1 cursor-pointer select-none"
                >
                  <input
                    id="signup-accept-terms"
                    type="checkbox"
                    required
                    checked={acceptedTerms}
                    onChange={(e) => setAcceptedTerms(e.target.checked)}
                    className="mt-0.5 rounded bg-[var(--bg-canvas)] border-white/20 text-[var(--brand-primary)] focus:ring-[var(--brand-primary)] focus:ring-offset-0 accent-[var(--brand-primary)] cursor-pointer"
                  />
                  <span className="leading-relaxed">
                    By signing up, you accept the <a href={PRIVACY_POLICY_URL} target="_blank" rel="noopener noreferrer" className="underline underline-offset-2 hover:text-[var(--text-primary)]">Privacy Policy</a> and <a href={TERMS_OF_USE_URL} target="_blank" rel="noopener noreferrer" className="underline underline-offset-2 hover:text-[var(--text-primary)]">Terms of Use</a>.
                  </span>
                </label>

                {/* Submit Sign Up */}
                <button
                  type="submit"
                  disabled={isSending || !email || !acceptedTerms}
                  aria-busy={isSending}
                  className="w-full py-3.5 px-4 rounded-xl bg-[var(--brand-primary)] text-[var(--brand-text)] font-semibold text-sm flex items-center justify-center gap-2 shadow-lg shadow-[var(--brand-primary)]/25 hover:brightness-110 active:scale-[0.99] disabled:opacity-50 disabled:pointer-events-none transition-all cursor-pointer mt-2"
                >
                  {isSending ? (
                    <>
                      <Loader2 className="w-4 h-4 animate-spin" />
                      <span>Creating Account...</span>
                    </>
                  ) : (
                    <>
                      <span>Create Account & Send Code</span>
                      <ArrowRight className="w-4 h-4" />
                    </>
                  )}
                </button>

              </form>
            )}
          </div>
        )}

        {/* Step 2: Verify Code */}
        {step === 2 && (
          <div className="space-y-6 animate-fade-in" onPaste={handlePaste}>
            {/* Header: Brand Mark + Pill Badge */}
            <div className="flex items-center justify-between">
              <div className="w-14 h-14 rounded-2xl bg-[var(--brand-primary)]/15 ring-1 ring-[var(--brand-ring)]/40 flex items-center justify-center shadow-lg shadow-[var(--brand-primary)]/20">
                <img
                  src="/accountgo-mark.svg"
                  alt="AccountGo"
                  className="w-9 h-9 rounded-xl"
                />
              </div>

              {/* Pill badge with back navigation */}
              <button
                type="button"
                onClick={() => {
                  setStep(1);
                  setErrorMessage(null);
                }}
                className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full text-xs font-semibold bg-[var(--brand-subtle)] text-[var(--brand-primary)] ring-1 ring-[var(--brand-ring)]/30 hover:bg-[var(--brand-subtle)]/80 transition-all cursor-pointer"
                title="Change details"
              >
                <ArrowLeft className="w-3.5 h-3.5" />
                <span>AccountGo Cloud™</span>
              </button>
            </div>

            {/* Heading & Subtitle */}
            <div className="space-y-1.5">
              <h1 className="text-2xl sm:text-3xl font-bold tracking-tight text-[var(--text-main)]">
                {mode === 'signup' ? 'Verify Your Account' : 'Verify Code'}
              </h1>
              <p className="text-sm text-[var(--text-secondary)] leading-relaxed">
                We sent a 6-digit verification code to{' '}
                <span className="font-semibold text-[var(--text-main)] break-all">{email}</span>.
              </p>
            </div>

            {/* Error Banner */}
            {errorMessage && (
              <div
                role="alert"
                aria-live="assertive"
                className="flex items-start gap-2.5 p-3 rounded-xl bg-rose-500/10 text-rose-400 text-xs font-medium ring-1 ring-rose-500/20 animate-fade-in"
              >
                <AlertCircle className="w-4 h-4 flex-shrink-0 mt-0.5" />
                <span className="flex-1">{errorMessage}</span>
              </div>
            )}

            {/* Dev Mode Code Auto-Fill Banner */}
            {devCode && (
              <div className="p-3 rounded-xl bg-amber-500/10 border border-amber-500/30 text-amber-300 text-xs flex items-center justify-between animate-fade-in">
                <div className="flex items-center gap-2">
                  <Sparkles className="w-4 h-4 text-amber-400 flex-shrink-0" />
                  <span>
                    Dev Code: <strong className="font-mono text-sm tracking-wider text-amber-200">{devCode}</strong>
                  </span>
                </div>
                <span className="text-[11px] bg-amber-500/20 px-2 py-0.5 rounded text-amber-200 font-medium">Auto-filled</span>
              </div>
            )}

            {/* 6-Digit OTP Boxes */}
            <div className="space-y-2">
              <div className="flex items-center justify-between text-xs font-semibold text-[var(--text-secondary)]">
                <span>Enter 6-digit code</span>
                {expirySeconds > 0 ? (
                  <span className="text-[11px] text-[var(--text-muted)] flex items-center gap-1 font-mono">
                    <Clock className="w-3 h-3 text-[var(--text-muted)]" />
                    <span>Expires in {formatTime(expirySeconds)}</span>
                  </span>
                ) : (
                  <span className="text-[11px] text-rose-400 font-semibold">
                    Code expired
                  </span>
                )}
              </div>

              {/* 6 Inputs Grid */}
              <div className="grid grid-cols-6 gap-2 sm:gap-2.5">
                {otp.map((digit, idx) => (
                  <input
                    key={idx}
                    ref={(el) => {
                      inputRefs.current[idx] = el;
                    }}
                    type="text"
                    inputMode="numeric"
                    autoComplete="one-time-code"
                    pattern="[0-9]*"
                    maxLength={1}
                    value={digit}
                    onChange={(e) => handleOtpChange(idx, e.target.value)}
                    onKeyDown={(e) => handleKeyDown(idx, e)}
                    disabled={isVerifying}
                    aria-label={`Verification code digit ${idx + 1} of 6`}
                    className="w-full aspect-[4/5] sm:h-16 text-center text-xl sm:text-2xl font-bold font-mono rounded-xl sm:rounded-2xl bg-[var(--bg-canvas)] text-[var(--text-main)] ring-1 ring-white/10 focus:ring-2 focus:ring-[var(--brand-primary)] focus:outline-none transition-all shadow-inner caret-[var(--brand-primary)] disabled:opacity-50"
                  />
                ))}
              </div>
            </div>

            {/* Turnstile Bot Protection */}
            <div className="flex justify-center pt-1">
              <TurnstileWidget
                ref={verifyTurnstileRef}
                action="verify_otp"
                onVerify={(token) => setVerifyTurnstileToken(token)}
                onExpire={() => setVerifyTurnstileToken('')}
              />
            </div>

            {/* Verify Action Button */}
            <button
              type="button"
              onClick={() => handleVerifyCode()}
              disabled={!isOtpComplete || isVerifying || expirySeconds === 0}
              aria-busy={isVerifying}
              className="w-full py-3.5 px-4 rounded-xl bg-[var(--brand-primary)] text-[var(--brand-text)] font-semibold text-sm flex items-center justify-center gap-2 shadow-lg shadow-[var(--brand-primary)]/25 hover:brightness-110 active:scale-[0.99] disabled:opacity-50 disabled:pointer-events-none transition-all cursor-pointer"
            >
              {isVerifying ? (
                <>
                  <Loader2 className="w-4 h-4 animate-spin" />
                  <span>Verifying Code...</span>
                </>
              ) : (
                <>
                  <span>{mode === 'signup' ? 'Complete Sign-Up & Enter' : 'Verify & Enter AccountGo'}</span>
                  <ArrowRight className="w-4 h-4" />
                </>
              )}
            </button>

            {/* Bottom Actions: Change Email & Resend Code */}
            <div className="flex items-center justify-between text-xs pt-2">
              <button
                type="button"
                onClick={() => {
                  setStep(1);
                  setErrorMessage(null);
                }}
                className="flex items-center gap-1.5 text-[var(--text-secondary)] hover:text-[var(--text-main)] transition-colors cursor-pointer"
              >
                <ArrowLeft className="w-3.5 h-3.5" />
                <span>Change details</span>
              </button>

              <button
                type="button"
                onClick={() => handleSendCode()}
                disabled={resendCooldown > 0 || isSending}
                className={`transition-colors font-medium cursor-pointer ${
                  resendCooldown > 0
                    ? 'text-[var(--text-muted)] cursor-not-allowed'
                    : 'text-[var(--brand-primary)] hover:underline'
                }`}
              >
                {resendCooldown > 0 ? `Resend code in ${resendCooldown}s` : 'Resend code'}
              </button>
            </div>

            {/* Dev Mode Notification */}
            {import.meta.env.DEV && (
              <div className="p-2.5 rounded-xl bg-[var(--bg-subtle)] text-[11px] text-[var(--text-muted)] flex items-center justify-between gap-2">
                <div className="flex items-center gap-1.5 truncate">
                  <Terminal className="w-3.5 h-3.5 text-[var(--brand-primary)] flex-shrink-0" />
                  <span className="truncate">
                    {devCode ? 'Dev Mode: Code auto-filled from API' : 'Local Dev: Code is logged to API terminal output'}
                  </span>
                </div>
              </div>
            )}
          </div>
        )}

        {/* Step: MFA Challenge */}
        {step === 'mfa_challenge' && mfaChallengeData && (
          <MfaChallenge
            mfaToken={mfaChallengeData.mfaToken}
            methods={mfaChallengeData.methods}
            onSuccess={async (authResponse) => {
              completeSession(authResponse);
              await handlePostAuthSuccess();
            }}
            onCancel={() => {
              setStep(googleMfa.current ? 1 : 2);
              setMfaChallengeData(null);
              setErrorMessage(null);
            }}
          />
        )}

        {/* Step: MFA Enrollment Wizard (forced by policy) */}
        {step === 'mfa_enroll' && mfaEnrollData && (
          <MfaEnrollWizard
            enrollToken={mfaEnrollData.mfaToken}
            onSuccess={async (authResponse) => {
              completeSession(authResponse);
              await handlePostAuthSuccess();
            }}
            onCancel={() => {
              setStep(googleMfa.current ? 1 : 2);
              setMfaEnrollData(null);
              setErrorMessage(null);
            }}
          />
        )}
      </div>
    </div>
  );
};

export default Login;
