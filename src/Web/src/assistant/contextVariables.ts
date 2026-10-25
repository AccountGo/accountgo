export type AssistantPageContext = {
  pageRoute?: string;
  pageTitle?: string;
  organizationId?: string;
  companySlug?: string;
  companyId?: string;
};

const SERVER_OWNED_KEYS = new Set([
  'userId',
  'sessionId',
  'organizationId',
  'companyId',
  'companySlug',
]);

/**
 * Merge SDK payload CVs with page context. Tenant/identity keys are never taken
 * from the SDK payload — the API overwrites them from JWT + ITenantContext.
 * Page route/title prefer the live page context over stale payload values.
 */
export function mergeAssistantContextVariables(
  pageContext: AssistantPageContext,
  payloadVars?: Record<string, string>,
): Record<string, string> {
  const merged: Record<string, string> = {};
  for (const [key, value] of Object.entries(payloadVars || {})) {
    if (!key || value == null) continue;
    if (SERVER_OWNED_KEYS.has(key)) continue;
    merged[key] = value;
  }

  if (pageContext.pageRoute) merged.pageRoute = pageContext.pageRoute;
  if (pageContext.pageTitle) merged.pageTitle = pageContext.pageTitle;
  return merged;
}

export function buildRunStreamBody(
  message: string,
  sessionId: string | undefined,
  contextVariables: Record<string, string>,
): Record<string, unknown> {
  const cvs = { ...contextVariables };
  if (sessionId) {
    cvs.sessionId = sessionId;
  }
  const body: Record<string, unknown> = {
    message,
    contextVariables: cvs,
  };
  if (sessionId) {
    body.sessionId = sessionId;
  }
  return body;
}
