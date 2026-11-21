/**
 * Message for a write that failed because the service answered 502, 503 or 504 without a readable problem body
 * (a cold start, a proxy error page). Returns null for any other status so the caller keeps its own message.
 * The wording says plainly that nothing was saved, so the user knows to repeat the change.
 */
export function unavailableWriteMessage(status: number, parsedMessage?: string | null): string | null {
  if (status !== 502 && status !== 503 && status !== 504) return null;
  if (parsedMessage && parsedMessage.trim()) return null;
  return 'The service is starting or temporarily unavailable, so your change was not saved. Wait a moment and try again.';
}
