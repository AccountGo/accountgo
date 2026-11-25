/**
 * One place that turns an API failure into a category the UI can act on.
 *
 * The API answers with RFC 7807 problem documents. Where a `code` exists it decides the category; the status alone is only
 * used where the server sends no code (a 401, or a 403 nobody has named yet). Payload shapes are taken from:
 *   - permission denial  → src/Api/Filters/AuthorizationFilters.cs (RequirePermissionAttribute.Denied):
 *     403 { title: "Access Denied", code: "PERMISSION_DENIED", requiredPermission, policyId?, policyVersion? }
 *   - plan and quota     → src/Api/Filters/EntitlementProblemFactory.cs: 403 { code: QUOTA_EXCEEDED | MODULE_NOT_SUBSCRIBED |
 *     PLAN_NOT_AVAILABLE | SUBSCRIPTION_INACTIVE | FEATURE_NOT_ENTITLED, resource?, limit?, used?, ... }
 *   - MFA step-up        → src/Api/Filters/MfaAuthorizationMiddlewareResultHandler.cs: 403 { code: "mfa_step_up_required" }
 *   - books not ready    → 409 { code: "BOOKS_NOT_READY", unpostedInvoices, unpostedBills }
 *   - workspace lost     → src/Api/Middleware/TenantResolutionMiddleware.cs: 404 { code: "WORKSPACE_NOT_FOUND" }
 *     (older servers sent the same body without a code, only title "Workspace not found")
 * Record-level 404s are deliberately not classified: cross-tenant records stay a plain "not found".
 */

/** Problem codes the server returns when the plan, not the user's role, blocks an action. */
export const ENTITLEMENT_CODES = [
  'QUOTA_EXCEEDED',
  'MODULE_NOT_SUBSCRIBED',
  'PLAN_NOT_AVAILABLE',
  'SUBSCRIPTION_INACTIVE',
  'FEATURE_NOT_ENTITLED',
] as const;
export type EntitlementCode = (typeof ENTITLEMENT_CODES)[number];

export interface EntitlementProblem {
  code: EntitlementCode;
  title?: string;
  detail?: string;
  resource?: string;
  limit?: number;
  used?: number;
  period?: string | null;
  contact?: string | null;
  requiredModule?: string;
  requiredFeature?: string;
  effectivePlan?: string;
  canManageBilling?: boolean;
  upgradeAvailable?: boolean;
  monthly?: boolean;
  status?: string;
}

export interface UnpostedDocument {
  type: 'invoice' | 'bill';
  id: string;
  number?: string | null;
  date: string;
}

export interface BooksNotReadyProblem {
  code: 'BOOKS_NOT_READY';
  title?: string;
  detail?: string;
  unpostedInvoices: UnpostedDocument[];
  unpostedBills: UnpostedDocument[];
}

export function isBooksNotReadyProblem(body: unknown): body is BooksNotReadyProblem {
  const value = body as Partial<BooksNotReadyProblem> | null;
  return value?.code === 'BOOKS_NOT_READY' && Array.isArray(value.unpostedInvoices) && Array.isArray(value.unpostedBills);
}

export function isEntitlementProblem(body: unknown): body is EntitlementProblem {
  const code = (body as { code?: unknown } | null)?.code;
  return typeof code === 'string' && (ENTITLEMENT_CODES as readonly string[]).includes(code);
}

export type ProblemCategory =
  | 'session-expired'
  | 'permission-denied'
  | 'plan-required'
  | 'quota-exceeded'
  | 'step-up-required'
  | 'books-not-ready'
  | 'workspace-access-lost'
  | 'unknown-forbidden';

export interface ClassifiedProblem {
  category: ProblemCategory;
  status: number;
  /** The server's problem code, when it sent one. */
  code?: string;
  /** For permission-denied: the permission the server says is missing. */
  requiredPermission?: string;
  /** For a denial raised by the client route guard: every permission that would have opened the page (any one of them). */
  requiredAnyOf?: string[];
  requestId?: string;
  title?: string;
  detail?: string;
  entitlement?: EntitlementProblem;
  booksNotReady?: BooksNotReadyProblem;
}

function asRecord(body: unknown): Record<string, unknown> | null {
  return body !== null && typeof body === 'object' && !Array.isArray(body) ? (body as Record<string, unknown>) : null;
}

function text(value: unknown): string | undefined {
  return typeof value === 'string' && value.length > 0 ? value : undefined;
}

/**
 * Classifies a failed response from its status and parsed JSON body (null when there is none). Returns null for anything the
 * UI should treat as an ordinary failure: record 404s, validation errors, server errors and successes.
 */
export function classifyApiProblem(status: number, body: unknown, requestIdHeader?: string | null): ClassifiedProblem | null {
  const record = asRecord(body);
  const code = text(record?.code);
  const base = {
    status,
    code,
    requestId: text(record?.requestId) ?? text(record?.traceId) ?? text(requestIdHeader),
    title: text(record?.title),
    detail: text(record?.detail),
  };

  if (status === 401) return { category: 'session-expired', ...base };

  if (status === 403) {
    if (code === 'PERMISSION_DENIED') {
      return { category: 'permission-denied', ...base, requiredPermission: text(record?.requiredPermission) };
    }
    if (code === 'mfa_step_up_required') return { category: 'step-up-required', ...base };
    if (isEntitlementProblem(record)) {
      return {
        category: record.code === 'QUOTA_EXCEEDED' ? 'quota-exceeded' : 'plan-required',
        ...base,
        entitlement: record,
      };
    }
    return { category: 'unknown-forbidden', ...base };
  }

  if (status === 409 && isBooksNotReadyProblem(record)) {
    return { category: 'books-not-ready', ...base, booksNotReady: record };
  }

  // A workspace that does not exist and one the caller is not a member of answer identically on purpose. The code names it;
  // the title is the fallback for a server that predates the code.
  if (status === 404 && (code === 'WORKSPACE_NOT_FOUND' || (code === undefined && text(record?.title) === 'Workspace not found'))) {
    return { category: 'workspace-access-lost', ...base };
  }

  return null;
}

/** Reads a failed response (without consuming it) and classifies it. A body that is not JSON counts as no body. */
export async function classifyResponse(response: Response): Promise<ClassifiedProblem | null> {
  if (response.ok) return null;
  let body: unknown = null;
  const type = response.headers.get('content-type') || '';
  if (type.includes('json')) {
    try {
      body = await response.clone().json();
    } catch {
      body = null;
    }
  }
  return classifyApiProblem(response.status, body, response.headers.get('x-request-id'));
}

/** What a page may say instead of a status code. Plan wording stays in the plan dialog; these lines never name a plan. */
export function describeProblem(problem: ClassifiedProblem | null, fallback: string): string {
  switch (problem?.category) {
    case 'session-expired':
      return 'Your session ended. Sign in again to continue.';
    case 'permission-denied':
      // A short summary for a failed action; the notice beside it carries the roles and the next step.
      return "You don't have access to do that.";
    case 'unknown-forbidden':
      return "You don't have access to this.";
    case 'plan-required':
    case 'quota-exceeded':
      return "This isn't available on your current plan.";
    case 'step-up-required':
      return 'Confirm it is you to continue.';
    case 'books-not-ready':
      return 'Some documents need attention before this can continue.';
    case 'workspace-access-lost':
      return "You don't have access to this workspace.";
    default:
      return fallback;
  }
}

/**
 * The server's own sentence for a problem (detail, message or title), except for a permission denial: the server's wording there names
 * the permission key, so it is replaced by the short line. For callers that read a problem body themselves.
 */
export function problemText(status: number, body: unknown, fallback: string): string {
  const record = asRecord(body);
  const classified = classifyApiProblem(status, body);
  if (classified?.category === 'permission-denied') return describeProblem(classified, fallback);
  return text(record?.detail) ?? text(record?.message) ?? text(record?.title) ?? fallback;
}

/** A load or action failure that carries its classification, so a page can swap in the "No access" view. */
export class ProblemError extends Error {
  constructor(message: string, readonly problem: ClassifiedProblem | null) {
    super(message);
    this.name = 'ProblemError';
  }
}

/** Builds the error for a failed response: the classifier's wording when it recognises it, the page's own line otherwise. Never a status code. */
export async function problemError(response: Response, fallback: string): Promise<ProblemError> {
  const problem = await classifyResponse(response);
  return new ProblemError(describeProblem(problem, fallback), problem);
}

/** The permission-denied problem inside an error, if that is what it is. */
export function deniedProblemOf(error: unknown): ClassifiedProblem | null {
  return error instanceof ProblemError && error.problem?.category === 'permission-denied' ? error.problem : null;
}

/**
 * The line to show for a failed action (save, pay, void…): the classifier's wording when it recognises the failure, otherwise
 * the server's own message when it sent one, otherwise the caller's fallback. Never a status code.
 */
export async function failureMessage(response: Response, fallback: string): Promise<string> {
  const problem = await classifyResponse(response);
  if (problem) return describeProblem(problem, fallback);
  try {
    const body = asRecord(await response.clone().json());
    return text(body?.message) ?? text(body?.detail) ?? fallback;
  } catch {
    return fallback;
  }
}

/** True when the server asked for a fresh second factor (403 mfa_step_up_required). The step-up prompt keeps its own flow. */
export function isStepUpRequired(status: number, body: unknown): boolean {
  return classifyApiProblem(status, body)?.category === 'step-up-required';
}
