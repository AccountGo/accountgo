import { describe, expect, it } from 'vitest';
import {
  classifyApiProblem,
  classifyResponse,
  describeProblem,
  deniedProblemOf,
  failureMessage,
  problemError,
} from '../src/lib/apiProblems';

// Payload shapes are copied from the API:
//   permission denial  src/Api/Filters/AuthorizationFilters.cs  RequirePermissionAttribute.Denied
//   plan and quota     src/Api/Filters/EntitlementProblemFactory.cs
//   step-up            src/Api/Filters/MfaAuthorizationMiddlewareResultHandler.cs
//   books not ready    409 BOOKS_NOT_READY (unpostedInvoices / unpostedBills)
//   workspace lost     src/Api/Middleware/TenantResolutionMiddleware.cs WriteNotFoundAsync
const permissionDenied = {
  type: 'about:blank',
  title: 'Access Denied',
  status: 403,
  code: 'PERMISSION_DENIED',
  requiredPermission: 'purchases.vendors.view',
  policyId: 'POL-X',
  policyVersion: '1',
};
const quota = { title: 'Plan limit reached', status: 403, code: 'QUOTA_EXCEEDED', resource: 'bills', limit: 20, used: 20 };
const booksNotReady = { code: 'BOOKS_NOT_READY', title: 'Books not ready', unpostedInvoices: [], unpostedBills: [] };

describe('classifyApiProblem', () => {
  it('names a permission denial and carries the missing permission', () => {
    expect(classifyApiProblem(403, permissionDenied)).toMatchObject({
      category: 'permission-denied',
      requiredPermission: 'purchases.vendors.view',
      code: 'PERMISSION_DENIED',
      status: 403,
    });
  });

  it.each(['MODULE_NOT_SUBSCRIBED', 'PLAN_NOT_AVAILABLE', 'SUBSCRIPTION_INACTIVE', 'FEATURE_NOT_ENTITLED'])('%s is plan-required', (code) => {
    const result = classifyApiProblem(403, { code, title: 'x' });
    expect(result?.category).toBe('plan-required');
    expect(result?.entitlement?.code).toBe(code);
  });

  it('QUOTA_EXCEEDED is quota-exceeded and keeps the entitlement fields', () => {
    const result = classifyApiProblem(403, quota);
    expect(result?.category).toBe('quota-exceeded');
    expect(result?.entitlement).toMatchObject({ resource: 'bills', limit: 20, used: 20 });
  });

  it('mfa_step_up_required is step-up-required', () => {
    expect(classifyApiProblem(403, { code: 'mfa_step_up_required', detail: 'Step-up authentication is required for this operation.' })?.category)
      .toBe('step-up-required');
  });

  it('BOOKS_NOT_READY with both lists is books-not-ready, and without them is not', () => {
    expect(classifyApiProblem(409, booksNotReady)?.category).toBe('books-not-ready');
    expect(classifyApiProblem(409, { code: 'BOOKS_NOT_READY' })).toBeNull();
    expect(classifyApiProblem(409, { code: 'SOMETHING_ELSE' })).toBeNull();
  });

  it('any 401 is session-expired, with or without a body', () => {
    expect(classifyApiProblem(401, { title: 'Unauthorized', status: 401 })?.category).toBe('session-expired');
    expect(classifyApiProblem(401, null)?.category).toBe('session-expired');
  });

  it('the workspace 404 is workspace-access-lost by code, and by title for an older server', () => {
    expect(classifyApiProblem(404, { title: 'Workspace not found', status: 404, code: 'WORKSPACE_NOT_FOUND' })?.category).toBe('workspace-access-lost');
    expect(classifyApiProblem(404, { title: 'Workspace not found', status: 404 })?.category).toBe('workspace-access-lost');
  });

  it('a record-level 404 is not classified, so the page keeps its own Not found', () => {
    expect(classifyApiProblem(404, { title: 'Not Found', status: 404 })).toBeNull();
    expect(classifyApiProblem(404, null)).toBeNull();
    expect(classifyApiProblem(404, { title: 'Workspace not found', code: 'OTHER' })).toBeNull();
  });

  it('a 403 with no recognised code is unknown-forbidden, never a guess', () => {
    expect(classifyApiProblem(403, null)?.category).toBe('unknown-forbidden');
    expect(classifyApiProblem(403, { title: 'Forbidden' })?.category).toBe('unknown-forbidden');
    expect(classifyApiProblem(403, { code: 'SOMETHING_NEW' })?.category).toBe('unknown-forbidden');
    expect(classifyApiProblem(403, 'plain text')?.category).toBe('unknown-forbidden');
  });

  it('validation errors, server errors and successes are not classified', () => {
    expect(classifyApiProblem(400, { title: 'Bad', errors: {} })).toBeNull();
    expect(classifyApiProblem(422, { code: 'PERMISSION_DENIED' })).toBeNull();
    expect(classifyApiProblem(500, { title: 'Boom' })).toBeNull();
    expect(classifyApiProblem(503, { reasonCode: 'IncompatiblePolicySchema' })).toBeNull();
    expect(classifyApiProblem(200, {})).toBeNull();
  });

  it('a code decides over the status: a permission code on a non-403 is not a denial', () => {
    expect(classifyApiProblem(409, permissionDenied)).toBeNull();
  });

  it('keeps a request id from the body or the header', () => {
    expect(classifyApiProblem(403, { ...permissionDenied, requestId: 'req-1' })?.requestId).toBe('req-1');
    expect(classifyApiProblem(403, permissionDenied, 'hdr-9')?.requestId).toBe('hdr-9');
  });
});

describe('classifyResponse and the error helpers', () => {
  const json = (status: number, body: unknown) =>
    new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/problem+json' } });

  it('reads a JSON problem without consuming the response', async () => {
    const res = json(403, permissionDenied);
    expect((await classifyResponse(res))?.category).toBe('permission-denied');
    expect((await res.json()).code).toBe('PERMISSION_DENIED');
  });

  it('treats a body that is not JSON as no body', async () => {
    expect((await classifyResponse(new Response('Forbidden', { status: 403 })))?.category).toBe('unknown-forbidden');
    expect(await classifyResponse(new Response('boom', { status: 500 }))).toBeNull();
    expect(await classifyResponse(new Response('{}', { status: 200 }))).toBeNull();
  });

  it('problemError says what happened without a status code and flags a denial for the page', async () => {
    const denied = await problemError(json(403, permissionDenied), 'Vendors could not be loaded.');
    expect(denied.message).not.toMatch(/40[13]/);
    expect(deniedProblemOf(denied)?.requiredPermission).toBe('purchases.vendors.view');

    const failed = await problemError(json(500, { title: 'Boom' }), 'Vendors could not be loaded.');
    expect(failed.message).toBe('Vendors could not be loaded.');
    expect(deniedProblemOf(failed)).toBeNull();
    expect(deniedProblemOf(new Error('x'))).toBeNull();
  });

  it('failureMessage prefers the classifier, then the server message, then the fallback', async () => {
    expect(await failureMessage(json(403, quota), 'fallback')).toBe("This isn't available on your current plan.");
    expect(await failureMessage(json(400, { message: 'Amount must be positive.' }), 'fallback')).toBe('Amount must be positive.');
    expect(await failureMessage(json(400, { detail: 'Bad date.' }), 'fallback')).toBe('Bad date.');
    expect(await failureMessage(new Response('x', { status: 500 }), 'fallback')).toBe('fallback');
  });

  it('describeProblem never contains a status code', () => {
    for (const category of ['session-expired', 'permission-denied', 'plan-required', 'quota-exceeded', 'step-up-required', 'books-not-ready', 'workspace-access-lost', 'unknown-forbidden'] as const) {
      expect(describeProblem({ category, status: 403 }, 'f')).not.toMatch(/\b40[0-9]\b/);
    }
    expect(describeProblem(null, 'The thing could not be loaded.')).toBe('The thing could not be loaded.');
  });
});
