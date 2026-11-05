import { useCallback, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { clearPersistedOrgSlug } from './storage';

/**
 * Deletes the current workspace (DELETE /api/organizations/current). When the server asks for a recent
 * second-factor check (403 mfa_step_up_required), `needsStepUp` turns on; after the caller's step-up prompt
 * verifies, `retry()` repeats the delete with the same confirmation slug.
 */
export const useDeleteCurrentWorkspace = () => {
  const { authFetch } = useAuth();
  const navigate = useNavigate();
  const [deleting, setDeleting] = useState(false);
  const [error, setError] = useState('');
  const [needsStepUp, setNeedsStepUp] = useState(false);
  const lastSlug = useRef('');

  const remove = useCallback(async (confirmSlug: string) => {
    lastSlug.current = confirmSlug;
    setDeleting(true);
    setError('');
    try {
      const response = await authFetch('/api/organizations/current', {
        method: 'DELETE',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ confirmSlug }),
      });
      if (!response.ok) {
        const problem = await response.json().catch(() => null) as { code?: string; detail?: string; title?: string } | null;
        if (response.status === 403 && problem?.code === 'mfa_step_up_required') {
          setNeedsStepUp(true);
          return;
        }
        setError(problem?.detail || problem?.title || 'Workspace could not be deleted.');
        return;
      }
      setNeedsStepUp(false);
      clearPersistedOrgSlug();
      navigate('/select-org', { replace: true, state: { notice: 'Workspace deleted.' } });
    } catch {
      setError('Workspace could not be deleted. Please try again.');
    } finally {
      setDeleting(false);
    }
  }, [authFetch, navigate]);

  const retry = useCallback(() => {
    setNeedsStepUp(false);
    void remove(lastSlug.current);
  }, [remove]);

  const reset = useCallback(() => {
    setError('');
    setNeedsStepUp(false);
  }, []);

  return { deleting, error, needsStepUp, remove, retry, reset };
};
