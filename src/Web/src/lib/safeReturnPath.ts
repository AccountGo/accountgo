/**
 * Where to send someone after they sign in again. Only an in-app path is allowed: a crafted value in navigation state must
 * never take the browser to another site or into a sign-in screen. Returns null when the value is not safe, and the caller
 * falls back to the normal post-sign-in destination.
 */
const AUTH_PATHS = /^\/(login|signup|verify|verify-otp|logout|auth|select-org)(\/|$)/i;

export function safeReturnPath(pathname: unknown, search?: unknown): string | null {
  if (typeof pathname !== 'string' || pathname.length === 0 || pathname.length > 2048) return null;
  const query = typeof search === 'string' ? search : '';
  if (query && !query.startsWith('?')) return null;
  const path = pathname + query;

  if (!path.startsWith('/') || path.startsWith('//')) return null; // relative, or protocol-relative (//host)
  if (/[\u0000-\u001f\u007f\\]/.test(path)) return null; // control characters, backslashes
  if (/^\/[a-z][a-z0-9+.-]*:/i.test(path)) return null; // "/javascript:..." style

  // A path whose decoded form differs may hide a different destination (%2F%2Fhost, %5Chost, %2e%2e).
  let decoded: string;
  try {
    decoded = decodeURIComponent(pathname);
  } catch {
    return null;
  }
  if (decoded !== pathname) {
    if (decoded.startsWith('//') || /[\u0000-\u001f\u007f\\]/.test(decoded) || /(^|\/)\.\.(\/|$)/.test(decoded)) return null;
    if (AUTH_PATHS.test(decoded)) return null;
  }
  if (/(^|\/)\.\.(\/|$)/.test(pathname)) return null;
  if (AUTH_PATHS.test(pathname)) return null;
  return path;
}
