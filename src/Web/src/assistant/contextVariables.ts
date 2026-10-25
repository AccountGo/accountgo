export type AssistantPageContext = {
  pageRoute?: string;
  pageTitle?: string;
  organizationId?: string;
  companySlug?: string;
  companyId?: string;
};

/** Merge SDK payload CVs with the current page/tenant context for Anter. */
export function mergeAssistantContextVariables(
  pageContext: AssistantPageContext,
  payloadVars?: Record<string, string>,
): Record<string, string> {
  const merged: Record<string, string> = { ...(payloadVars || {}) };
  const ctx = pageContext;
  if (ctx.pageRoute && !merged.pageRoute) merged.pageRoute = ctx.pageRoute;
  if (ctx.pageTitle && !merged.pageTitle) merged.pageTitle = ctx.pageTitle;
  if (ctx.organizationId && !merged.organizationId) merged.organizationId = ctx.organizationId;
  if (ctx.companySlug && !merged.companySlug) merged.companySlug = ctx.companySlug;
  if (ctx.companyId && !merged.companyId) merged.companyId = ctx.companyId;
  return merged;
}

export function buildRunStreamBody(
  message: string,
  sessionId: string | undefined,
  contextVariables: Record<string, string>,
  organizationId?: string,
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
  if (organizationId) {
    body.organizationId = organizationId;
  }
  return body;
}
