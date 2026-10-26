export interface UserDto {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  roles: string[];
  permissions?: string[];
  phoneNumber?: string;
  hasPassword?: boolean;
}

export interface UserSession {
  id: number;
  createdAt: string;
  expiresAt: string;
  ipAddress?: string;
  userAgent?: string;
  isCurrent: boolean;
}

export interface AuthResponse {
  accessToken: string;
  expiresIn: number;
  user: UserDto;
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
}

export interface SignUpResponse {
  success: boolean;
  message: string;
  email: string;
  orgSlug?: string | null;
  expiresInSeconds: number;
  devCode?: string | null;
}

export interface AuthContextType {
  user: UserDto | null;
  accessToken: string | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  /** The API is starting up (after an update or a quiet spell); the session is kept while it does. */
  isStarting: boolean;
  sendOtp: (email: string) => Promise<OtpSendResponse>;
  signUp: (payload: SignUpPayload) => Promise<SignUpResponse>;
  verifyOtp: (email: string, otp: string) => Promise<void>;
  logout: () => Promise<void>;
  /** Drops the in-memory session. Use after the server has already revoked it. */
  clearSession: () => void;
  authFetch: (url: string, init?: RequestInit) => Promise<Response>;
  refreshUser: () => Promise<void>;
}
