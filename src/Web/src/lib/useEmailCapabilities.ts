import { useEffect, useState } from 'react';
import { useAuth } from '../auth/AuthContext';
import type { EmailCapabilities } from './customEmail';

/** Capabilities belong to the current organization; stale responses from a prior workspace are discarded. */
export function useEmailCapabilities(companyId: string, revision = 0) {
  const { authFetch } = useAuth();
  const [capabilities, setCapabilities] = useState<EmailCapabilities | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    let active = true;
    setCapabilities(null);
    setError(null);
    authFetch('/api/email/capabilities').then(async res => {
      const data = await res.json();
      if (!res.ok) throw new Error(data?.message || 'Email capabilities could not be loaded.');
      if (active && String(data.companyId) === companyId) setCapabilities(data);
    }).catch(err => { if (active) setError(err.message); });
    return () => { active = false; };
  }, [authFetch, companyId, revision]);
  return { capabilities, error };
}
