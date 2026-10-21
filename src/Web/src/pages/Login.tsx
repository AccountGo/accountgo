import React, { useState, useEffect, useRef } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { getPersistedOrgSlug, setPersistedOrgSlug } from '../lib/storage';
import {
  Mail,
  User,
  Building2,
  ArrowRight,
  ArrowLeft,
  Loader2,
  AlertCircle,
  Clock,
  Sparkles,
  Terminal,
} from 'lucide-react';

interface LoginProps {
  initialMode?: 'signin' | 'signup';
}

export const Login: React.FC<LoginProps> = ({ initialMode }) => {
  const navigate = useNavigate();
  const location = useLocation();
  const { isAuthenticated, isLoading: isAuthLoading, sendOtp, signUp, verifyOtp } = useAuth();

  // Mode state: 'signin' or 'signup'
  const [mode, setMode] = useState<'signin' | 'signup'>(() => {
    if (initialMode) return initialMode;
    if (location.pathname === '/signup') return 'signup';
    const params = new URLSearchParams(location.search);
    if (params.get('mode') === 'signup') return 'signup';
    return 'signin';
  });

  // Step state: 1 = Credentials request, 2 = 6-digit OTP verification
  const [step, setStep] = useState<1 | 2>(1);

  // Form fields
  const [email, setEmail] = useState<string>('admin@accountgo.ph');
  const [firstName, setFirstName] = useState<string>('');
  const [lastName, setLastName] = useState<string>('');
  const [orgName, setOrgName] = useState<string>('');
  const [orgSlug, setOrgSlug] = useState<string>('');
  const [targetOrgSlug, setTargetOrgSlug] = useState<string | null>(null);

  // OTP inputs
  const [otp, setOtp] = useState<string[]>(['', '', '', '', '', '']);
  const [devCode, setDevCode] = useState<string | null>(null);
  const [isSending, setIsSending] = useState<boolean>(false);
  const [isVerifying, setIsVerifying] = useState<boolean>(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  // Timers
  const [resendCooldown, setResendCooldown] = useState<number>(0);
  const [expirySeconds, setExpirySeconds] = useState<number>(300);

  // Input refs for the 6 OTP boxes
  const inputRefs = useRef<(HTMLInputElement | null)[]>([]);

  // If already authenticated, redirect away
  useEffect(() => {
    if (!isAuthLoading && isAuthenticated) {
      const fromPath = (location.state as any)?.from?.pathname;
      const lastSlug = getPersistedOrgSlug();
      if (fromPath && fromPath !== '/login' && fromPath !== '/signup') {
        navigate(fromPath, { replace: true });
      } else if (lastSlug) {
        navigate(`/org/${lastSlug}/dashboard`, { replace: true });
      } else {
        navigate('/select-org', { replace: true });
      }
    }
  }, [isAuthenticated, isAuthLoading, navigate, location.state]);

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

  // Auto-generate slug when organization name changes
  const handleOrgNameChange = (val: string) => {
    setOrgName(val);
    const generated = val
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-+|-+$/g, '');
    setOrgSlug(generated);
  };

  // Format seconds to mm:ss
  const formatTime = (seconds: number) => {
    const mins = Math.floor(seconds / 60);
    const secs = seconds % 60;
    return `${mins}:${secs.toString().padStart(2, '0')}`;
  };

  // Submit Step 1 (Sign In): Send OTP code
  const handleSendCode = async (e?: React.FormEvent) => {
    if (e) e.preventDefault();
    if (!email || !email.includes('@')) {
      setErrorMessage('Please enter a valid work email address.');
      return;
    }

    setErrorMessage(null);
    setIsSending(true);

    try {
      const res = await sendOtp(email);
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
      setErrorMessage(err.message || 'Failed to send verification code.');
    } finally {
      setIsSending(false);
    }
  };

  // Submit Step 1 (Sign Up): Register account & Send Welcome OTP
  const handleSignUp = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!firstName.trim()) {
      setErrorMessage('Please enter your first name.');
      return;
    }
    if (!lastName.trim()) {
      setErrorMessage('Please enter your last name.');
      return;
    }
    if (!email || !email.includes('@')) {
      setErrorMessage('Please enter a valid work email address.');
      return;
    }

    setErrorMessage(null);
    setIsSending(true);

    try {
      const res = await signUp({
        firstName: firstName.trim(),
        lastName: lastName.trim(),
        email: email.trim(),
        organizationName: orgName.trim() || undefined,
        organizationSlug: orgSlug.trim() || undefined,
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
      setErrorMessage(err.message || 'Registration failed. Please try again.');
    } finally {
      setIsSending(false);
    }
  };

  // Submit Step 2: Verify OTP code
  const handleVerifyCode = async (codeToVerify?: string) => {
    const code = codeToVerify || otp.join('');
    if (code.length !== 6 || isVerifying) return;

    setErrorMessage(null);
    setIsVerifying(true);

    try {
      await verifyOtp(email, code);

      // Successfully authenticated! Route to target organization or org selector
      const fromPath = (location.state as any)?.from?.pathname;
      const destinationSlug = targetOrgSlug || getPersistedOrgSlug();

      if (targetOrgSlug) {
        setPersistedOrgSlug(targetOrgSlug);
        navigate(`/org/${targetOrgSlug}/dashboard`, { replace: true });
      } else if (fromPath && fromPath !== '/login' && fromPath !== '/signup') {
        navigate(fromPath, { replace: true });
      } else if (destinationSlug) {
        navigate(`/org/${destinationSlug}/dashboard`, { replace: true });
      } else {
        navigate('/select-org', { replace: true });
      }
    } catch (err: any) {
      setErrorMessage(err.message || 'Invalid or expired verification code.');
    } finally {
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
      <div className="w-full max-w-md bg-[var(--bg-surface)] backdrop-blur-2xl rounded-3xl p-6 sm:p-8 shadow-2xl shadow-black/80 ring-1 ring-white/10 relative overflow-hidden">
        {/* Step 1: Sign In or Sign Up Form */}
        {step === 1 && (
          <div className="space-y-6 animate-fade-in">
            {/* Header: Brand Mark + Pill Badge */}
            <div className="flex items-center justify-between">
              <div className="w-14 h-14 rounded-2xl bg-[var(--brand-primary)]/15 ring-1 ring-[var(--brand-ring)]/40 flex items-center justify-center shadow-lg shadow-[var(--brand-primary)]/20">
                <img
                  src="/accountgo-mark.svg"
                  alt="AccountGo"
                  className="w-9 h-9 rounded-xl"
                />
              </div>

              <div className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-semibold bg-[var(--brand-subtle)] text-[var(--brand-primary)] ring-1 ring-[var(--brand-ring)]/30">
                <Sparkles className="w-3 h-3" />
                <span>Passwordless OTP</span>
              </div>
            </div>

            {/* Segmented Mode Selector: Sign In vs Create Account */}
            <div className="grid grid-cols-2 p-1 rounded-xl bg-[var(--bg-canvas)] shadow-inner ring-1 ring-white/10">
              <button
                type="button"
                onClick={() => {
                  setMode('signin');
                  setErrorMessage(null);
                }}
                className={`py-2 text-xs font-semibold rounded-lg transition-all cursor-pointer ${
                  mode === 'signin'
                    ? 'bg-[var(--brand-primary)] text-[var(--brand-text)] shadow-sm'
                    : 'text-[var(--text-secondary)] hover:text-[var(--text-main)]'
                }`}
              >
                Sign In
              </button>
              <button
                type="button"
                onClick={() => {
                  setMode('signup');
                  setErrorMessage(null);
                  if (email === 'admin@accountgo.ph') setEmail('');
                }}
                className={`py-2 text-xs font-semibold rounded-lg transition-all cursor-pointer ${
                  mode === 'signup'
                    ? 'bg-[var(--brand-primary)] text-[var(--brand-text)] shadow-sm'
                    : 'text-[var(--text-secondary)] hover:text-[var(--text-main)]'
                }`}
              >
                Create Account
              </button>
            </div>

            {/* Heading & Subtitle */}
            <div className="space-y-1.5">
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
              <p className="text-sm text-[var(--text-secondary)] leading-relaxed">
                {mode === 'signin'
                  ? 'Enter your registered work email to receive a single-use 6-digit sign-in code.'
                  : 'Start managing your financials, invoicing, and organization with modern double-entry standards.'}
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

                {/* Submit Button */}
                <button
                  type="submit"
                  disabled={isSending || !email}
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

                <div className="text-center pt-2">
                  <button
                    type="button"
                    onClick={() => {
                      setMode('signup');
                      setErrorMessage(null);
                      if (email === 'admin@accountgo.ph') setEmail('');
                    }}
                    className="text-xs text-[var(--text-secondary)] hover:text-[var(--brand-primary)] transition-colors cursor-pointer"
                  >
                    Don't have an account? <span className="font-semibold text-[var(--brand-primary)] underline">Create one now</span>
                  </button>
                </div>
              </form>
            )}

            {/* Form Mode: SIGN UP */}
            {mode === 'signup' && (
              <form onSubmit={handleSignUp} className="space-y-4">
                {/* First Name & Last Name (2 Cols) */}
                <div className="grid grid-cols-2 gap-3">
                  <div className="space-y-1.5">
                    <label
                      htmlFor="signup-first-name"
                      className="text-xs font-semibold text-[var(--text-secondary)] uppercase tracking-wider"
                    >
                      First Name
                    </label>
                    <div className="relative">
                      <User className="w-4 h-4 text-[var(--text-muted)] absolute left-3 top-1/2 -translate-y-1/2 pointer-events-none" />
                      <input
                        id="signup-first-name"
                        type="text"
                        required
                        value={firstName}
                        onChange={(e) => setFirstName(e.target.value)}
                        placeholder="Elena"
                        autoFocus
                        className="w-full pl-9 pr-3 py-2.5 rounded-xl bg-[var(--bg-canvas)] text-sm font-medium text-[var(--text-main)] placeholder-[var(--text-muted)] ring-1 ring-white/10 focus:ring-2 focus:ring-[var(--brand-primary)] focus:outline-none transition-all shadow-inner"
                      />
                    </div>
                  </div>

                  <div className="space-y-1.5">
                    <label
                      htmlFor="signup-last-name"
                      className="text-xs font-semibold text-[var(--text-secondary)] uppercase tracking-wider"
                    >
                      Last Name
                    </label>
                    <input
                      id="signup-last-name"
                      type="text"
                      required
                      value={lastName}
                      onChange={(e) => setLastName(e.target.value)}
                      placeholder="Reyes"
                      className="w-full px-3 py-2.5 rounded-xl bg-[var(--bg-canvas)] text-sm font-medium text-[var(--text-main)] placeholder-[var(--text-muted)] ring-1 ring-white/10 focus:ring-2 focus:ring-[var(--brand-primary)] focus:outline-none transition-all shadow-inner"
                    />
                  </div>
                </div>

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

                {/* Initial Organization / Workspace (Optional) */}
                <div className="space-y-1.5 pt-1">
                  <div className="flex items-center justify-between">
                    <label
                      htmlFor="signup-org-name"
                      className="text-xs font-semibold text-[var(--text-secondary)] uppercase tracking-wider"
                    >
                      Workspace Name
                    </label>
                    <span className="text-[10px] text-[var(--text-muted)] uppercase tracking-wider">Optional</span>
                  </div>
                  <div className="relative">
                    <Building2 className="w-4 h-4 text-[var(--text-muted)] absolute left-3.5 top-1/2 -translate-y-1/2 pointer-events-none" />
                    <input
                      id="signup-org-name"
                      type="text"
                      value={orgName}
                      onChange={(e) => handleOrgNameChange(e.target.value)}
                      placeholder="e.g. Pacific Rim Trading"
                      className="w-full pl-10 pr-4 py-2.5 rounded-xl bg-[var(--bg-canvas)] text-sm font-medium text-[var(--text-main)] placeholder-[var(--text-muted)] ring-1 ring-white/10 focus:ring-2 focus:ring-[var(--brand-primary)] focus:outline-none transition-all shadow-inner"
                    />
                  </div>
                  {orgSlug && (
                    <div className="flex items-center gap-1.5 text-[11px] text-[var(--text-muted)] font-mono pl-1">
                      <span>URL preview:</span>
                      <span className="text-[var(--brand-primary)]">/org/{orgSlug}</span>
                    </div>
                  )}
                </div>

                {/* Submit Sign Up */}
                <button
                  type="submit"
                  disabled={isSending || !email || !firstName || !lastName}
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

                <div className="text-center pt-2">
                  <button
                    type="button"
                    onClick={() => {
                      setMode('signin');
                      setErrorMessage(null);
                    }}
                    className="text-xs text-[var(--text-secondary)] hover:text-[var(--brand-primary)] transition-colors cursor-pointer"
                  >
                    Already have an account? <span className="font-semibold text-[var(--brand-primary)] underline">Sign In</span>
                  </button>
                </div>
              </form>
            )}

            {/* Bottom Security Note */}
            <div className="text-center pt-1">
              <span className="text-[11px] text-[var(--text-muted)]">
                Enterprise Multi-Tenant Isolation & Cloudflare Edge Resilience.
              </span>
            </div>
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
                <span>AccountGo Cloud</span>
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
            <div className="p-2.5 rounded-xl bg-[var(--bg-subtle)] text-[11px] text-[var(--text-muted)] flex items-center justify-between gap-2">
              <div className="flex items-center gap-1.5 truncate">
                <Terminal className="w-3.5 h-3.5 text-[var(--brand-primary)] flex-shrink-0" />
                <span className="truncate">
                  {devCode ? 'Dev Mode: Code auto-filled from API' : 'Local Dev: Code is logged to API terminal output'}
                </span>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
};

export default Login;
