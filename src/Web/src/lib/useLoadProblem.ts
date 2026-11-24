import { useCallback, useState } from 'react';
import { deniedProblemOf, type ClassifiedProblem } from './apiProblems';

/**
 * Lets a page that loads data say "you can't open this" instead of showing an error banner above an empty list.
 *
 *   const load = useLoadProblem();
 *   // in the load function:  load.reset();  …  if (!res.ok) throw await problemError(res, 'Vendors could not be loaded.');
 *   // in its catch:          setError(load.capture(err, 'Vendors could not be loaded.'));
 *   // in the render:         <NoAccessOr denied={load.denied}>…the page body…</NoAccessOr>
 */
export function useLoadProblem() {
  const [denied, setDenied] = useState<ClassifiedProblem | null>(null);

  const reset = useCallback(() => setDenied(null), []);

  /** Records a permission denial (so the page can swap in the No-access view) and returns the line to show for any failure. */
  const capture = useCallback((error: unknown, fallback: string): string => {
    const problem = deniedProblemOf(error);
    setDenied(problem);
    return error instanceof Error && error.message ? error.message : fallback;
  }, []);

  return { denied, reset, capture };
}
