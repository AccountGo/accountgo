export interface UserDto {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  roles: string[];
  permissions?: string[];
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
}

export interface AuthContextType {
  user: UserDto | null;
  accessToken: string | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  sendOtp: (email: string) => Promise<OtpSendResponse>;
  verifyOtp: (email: string, otp: string) => Promise<void>;
  logout: () => Promise<void>;
  authFetch: (url: string, init?: RequestInit) => Promise<Response>;
}
