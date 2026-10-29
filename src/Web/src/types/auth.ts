export interface UserDto {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  roles: string[];
  permissions?: string[];
  phoneNumber?: string;
  hasPassword?: boolean;
  mfaReEnrollRequired?: boolean;
  mfaGracePeriodEndsAt?: string | null;
}

export interface WebAuthnCredential {
  id: number;
  nickname: string;
  createdAt: string;
  lastUsedAt?: string | null;
  aaGuid?: string | null;
}

export interface UserSession {
  id: number;
  createdAt: string;
  expiresAt: string;
  ipAddress?: string;
  userAgent?: string;
  isCurrent: boolean;
}

export interface TrustedDevice {
  id: number;
  label: string;
  createdByIp?: string;
  createdAt: string;
  expiresAt: string;
  lastSeenAt: string;
  isCurrent?: boolean;
}

export interface AuthResponse {
  accessToken: string;
  expiresIn: number;
  user: UserDto;
  organizations?: { slug: string; name: string; role: string }[];
}

export interface OtpSendResponse {
  message: string;
  expiresInSeconds: number;
  devCode?: string | null;
  orgSlug?: string | null;
}

export interface SignUpPayload {
  email: string;
  firstName: string;
  lastName: string;
  organizationName?: string;
  organizationSlug?: string;
  currency?: string;
  invitationToken?: string;
  'cf-turnstile-response'?: string;
}

export interface SignUpResponse {
  success: boolean;
  message: string;
  email: string;
  orgSlug?: string | null;
  expiresInSeconds: number;
  devCode?: string | null;
}

export interface MfaRequiredResponse {
  status: 'mfa_required';
  mfaToken: string;
  expiresInSeconds: number;
  methods: string[];
}

export interface MfaEnrollmentRequiredResponse {
  status: 'mfa_enrollment_required';
  mfaToken: string;
  expiresInSeconds: number;
  policy: string;
}

export type VerifyOtpResult =
  | { type: 'success'; data: AuthResponse }
  | { type: 'mfa_required'; data: MfaRequiredResponse }
  | { type: 'mfa_enrollment_required'; data: MfaEnrollmentRequiredResponse };

export interface MfaMethodsResponse {
  mfaEnabled: boolean;
  totpConfigured: boolean;
  webAuthnConfigured: boolean;
  recoveryCodesRemaining: number;
  methods: string[];
}

export interface TotpBeginResponse {
  sharedKey: string;
  otpauthUrl: string;
}

export interface TotpConfirmResponse {
  recoveryCodes: string[];
  authResponse?: AuthResponse | null;
}

export interface SecurityCapabilities {
  mfaAvailable: boolean;
  upgradeAvailable: boolean;
  enrolled: boolean;
  methods: string[];
}

export interface AuthContextType {
  user: UserDto | null;
  accessToken: string | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  /** The API is starting up (after an update or a quiet spell); the session is kept while it does. */
  isStarting: boolean;
  sendOtp: (email: string, turnstileToken?: string) => Promise<OtpSendResponse>;
  signUp: (payload: SignUpPayload) => Promise<SignUpResponse>;
  verifyOtp: (email: string, otp: string, turnstileToken?: string) => Promise<VerifyOtpResult>;
  completeSession: (authData: AuthResponse) => void;
  completeStepUp: (newToken: string) => void;
  logout: () => Promise<void>;
  /** Drops the in-memory session. Use after the server has already revoked it. */
  clearSession: () => void;
  authFetch: (url: string, init?: RequestInit) => Promise<Response>;
  refreshUser: () => Promise<void>;
}
