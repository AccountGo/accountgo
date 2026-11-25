export {
  ENTITLEMENT_CODES,
  isBooksNotReadyProblem,
  isEntitlementProblem,
  type BooksNotReadyProblem,
  type EntitlementCode,
  type EntitlementProblem,
  type UnpostedDocument,
} from './apiProblems';
import { classifyResponse, type BooksNotReadyProblem, type EntitlementProblem } from './apiProblems';

export const ENTITLEMENT_DENIED_EVENT = 'accountgo:entitlement-denied';

/** Issued invoices or bills with no ledger entry block turning on accounting (409 BOOKS_NOT_READY). */
export const BOOKS_NOT_READY_EVENT = 'accountgo:books-not-ready';

let activeWorkspace: { slug: string; personal: boolean } | null = null;
export function setEntitlementWorkspace(slug: string, personal: boolean): () => void {
  const workspace = { slug, personal };
  activeWorkspace = workspace;
  return () => { if (activeWorkspace === workspace) activeWorkspace = null; };
}
export function isActivePersonalBooks(slug: string | null): boolean {
  return !!slug && activeWorkspace?.slug === slug && activeWorkspace.personal;
}


let suppressDenialDepth = 0;

/** Temporarily skip the shared plan-limit dialog (wizard / transfer show their own copy). */
export async function withSuppressedEntitlementDenial<T>(fn: () => Promise<T>): Promise<T> {
  suppressDenialDepth += 1;
  try {
    return await fn();
  } finally {
    suppressDenialDepth -= 1;
  }
}

/**
 * Inspects a response without consuming it and announces plan denials, so one dialog can explain them wherever
 * the request came from. Pages still show their own inline errors.
 */
export async function announceEntitlementDenial(response: Response): Promise<void> {
  if (suppressDenialDepth > 0) return;
  if ((response.status !== 403 && response.status !== 409) || typeof window === 'undefined') return;
  const problem = await classifyResponse(response);
  if (!problem) return;
  if (problem.category === 'books-not-ready' && problem.booksNotReady) {
    window.dispatchEvent(new CustomEvent<BooksNotReadyProblem>(BOOKS_NOT_READY_EVENT, { detail: problem.booksNotReady }));
  } else if ((problem.category === 'plan-required' || problem.category === 'quota-exceeded') && problem.entitlement) {
    window.dispatchEvent(new CustomEvent<EntitlementProblem>(ENTITLEMENT_DENIED_EVENT, { detail: problem.entitlement }));
  }
}
