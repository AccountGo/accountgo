/** Pure create-workspace response handling for the review step (testable without React). */

export type CreateOrgSubmitOutcome =
  | { kind: 'redirect-checkout'; url: string }
  | { kind: 'pending'; slug: string; checkoutError: string | null }
  | { kind: 'success'; slug: string }
  | { kind: 'quota-free-slot'; detail: string | null }
  | { kind: 'error'; detail: string; status: number };

export interface CreateOrgResponseBody {
  slug?: string;
  activationState?: string;
  checkoutUrl?: string | null;
  checkoutError?: string | null;
  code?: string;
  resource?: string;
  detail?: string;
  title?: string;
  message?: string;
}

export function interpretCreateOrgResponse(
  status: number,
  body: CreateOrgResponseBody | null,
  fallbackSlug: string,
): CreateOrgSubmitOutcome {
  if (status === 403 && body?.code === 'QUOTA_EXCEEDED' && body.resource === 'owned_workspaces') {
    return { kind: 'quota-free-slot', detail: body.detail ?? null };
  }

  if (status >= 400 || !body) {
    return {
      kind: 'error',
      status,
      detail: body?.detail || body?.title || body?.message || 'We could not create the workspace. Try again.',
    };
  }

  const slug = (body.slug || fallbackSlug).trim();
  if (body.checkoutUrl) {
    return { kind: 'redirect-checkout', url: body.checkoutUrl };
  }
  if (body.activationState === 'pending_checkout' || body.checkoutError) {
    return { kind: 'pending', slug, checkoutError: body.checkoutError ?? null };
  }
  return { kind: 'success', slug };
}

export function buildCreateReturnUrl(origin: string, slug: string, interval: 'month' | 'year'): string {
  const url = new URL(`/org/${encodeURIComponent(slug)}`, origin);
  // The billing provider appends checkout=success / checkout=cancel itself; adding it here would make a
  // cancelled checkout read as success (URLSearchParams.get returns the first value).
  url.searchParams.set('interval', interval);
  return url.toString();
}
