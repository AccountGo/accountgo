import { useCallback, useRef, useState } from 'react';
import { isStepUpRequired } from './apiProblems';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../auth/AuthContext';
import { clearPersistedOrgSlug } from './storage';

/** How the owner handled the workspace's documents before deleting it (the server rechecks). */
export interface DeleteDocumentsOptions {
  documentsExportConfirmed?: boolean;
  skipDocumentsExport?: boolean;
}

const DOCUMENT_DELETE_MESSAGES: Record<string, string> = {
  DOCUMENT_EXPORT_REQUIRED: 'Download all documents first, or choose to delete them with the workspace.',
  DOCUMENT_EXPORT_STALE: 'Documents changed after your last download. Download them again, or choose to delete them.',
  DOCUMENT_EXPORT_CONFIRMATION_REQUIRED: 'Confirm that you opened the downloaded ZIP, or choose to delete your documents.',
};

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
  const lastDocuments = useRef<DeleteDocumentsOptions>({});

  const remove = useCallback(async (confirmSlug: string, documents: DeleteDocumentsOptions = {}) => {
    lastSlug.current = confirmSlug;
    lastDocuments.current = documents;
    setDeleting(true);
    setError('');
    try {
      const response = await authFetch('/api/organizations/current', {
        method: 'DELETE',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ confirmSlug, ...documents }),
      });
      if (!response.ok) {
        const problem = await response.json().catch(() => null) as { code?: string; detail?: string; title?: string } | null;
        if (isStepUpRequired(response.status, problem)) {
          setNeedsStepUp(true);
          return;
        }
        setError(DOCUMENT_DELETE_MESSAGES[problem?.code ?? ''] ?? problem?.detail ?? problem?.title ?? 'Workspace could not be deleted.');
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
    void remove(lastSlug.current, lastDocuments.current);
  }, [remove]);

  const reset = useCallback(() => {
    setError('');
    setNeedsStepUp(false);
  }, []);

  return { deleting, error, needsStepUp, remove, retry, reset };
};
