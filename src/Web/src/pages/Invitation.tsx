import React, { useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { AlertCircle, Loader2, Mail, Users } from 'lucide-react';
import { InvitationPreview } from '../types';
import { useAuth } from '../auth/AuthContext';
import { clearInvitationToken, getInvitationToken, storeInvitationToken } from '../lib/invitationStorage';
import { setPersistedOrgSlug } from '../lib/storage';

type InviteState = 'loading' | 'ready' | 'invalid' | 'expired' | 'used' | 'error';

function maskEmail(email: string): string {
  const at = email.indexOf('@');
  return at <= 0 ? '' : `${email[0]}***${email.slice(at)}`;
}

export const Invitation: React.FC = () => {
  const { token: routeToken } = useParams<{ token: string }>();
  const navigate = useNavigate();
  const { authFetch, isAuthenticated, user, logout } = useAuth();
  const [token, setToken] = useState<string | null>(null);
  const [preview, setPreview] = useState<InvitationPreview | null>(null);
  const [state, setState] = useState<InviteState>('loading');
  const [message, setMessage] = useState('');
  const [accepting, setAccepting] = useState(false);

  useEffect(() => {
    const inviteToken = routeToken || getInvitationToken();
    if (!inviteToken || !/^[A-Za-z0-9_-]{43}$/.test(inviteToken)) {
      clearInvitationToken();
      setToken(null);
      setState('invalid');
      return;
    }

    setToken(inviteToken);
    if (window.location.pathname.includes(inviteToken)) {
      window.history.replaceState(window.history.state, '', '/invite');
    }

    let mounted = true;
    void (async () => {
      try {
        const response = await fetch(`/api/invitations/${encodeURIComponent(inviteToken)}`);
        if (!mounted) return;
        if (response.status === 404) {
          clearInvitationToken();
          setToken(null);
          setState('invalid');
          return;
        }
        if (response.status === 410) {
          clearInvitationToken();
          setToken(null);
          setState('expired');
          return;
        }
        if (!response.ok) {
          setState('error');
          return;
        }
        const data = await response.json() as InvitationPreview;
        if (!mounted) return;
        if (data.accepted || data.expired) {
          clearInvitationToken();
          setToken(null);
        } else {
          storeInvitationToken(inviteToken);
        }
        setPreview(data);
        setState(data.accepted ? 'used' : data.expired ? 'expired' : 'ready');
      } catch {
        if (mounted) setState('error');
      }
    })();

    return () => {
      mounted = false;
    };
  }, [routeToken]);

  const matchingEmail = Boolean(user?.email && preview && maskEmail(user.email).toLowerCase() === preview.emailMasked.toLowerCase());

  const accept = async () => {
    if (!token || !isAuthenticated || !matchingEmail || accepting) return;
    setAccepting(true);
    setMessage('');
    try {
      const response = await authFetch(`/api/invitations/${encodeURIComponent(token)}/accept`, { method: 'POST' });
      if (response.status === 403) {
        setMessage('This invitation was sent to a different email. Sign out and use the address shown on the invitation.');
        return;
      }
      if (response.status === 410) {
        clearInvitationToken();
        setToken(null);
        setState('expired');
        return;
      }
      if (response.status === 404) {
        clearInvitationToken();
        setToken(null);
        setState('invalid');
        return;
      }
      if (!response.ok) {
        setMessage(response.status === 409
          ? 'This account already belongs to the workspace.'
          : 'We could not accept this invitation. Please try again or use Pending invitations.');
        return;
      }
      const result = await response.json() as { slug: string };
      clearInvitationToken();
      setToken(null);
      setPersistedOrgSlug(result.slug);
      navigate(`/org/${encodeURIComponent(result.slug)}/dashboard`, { replace: true });
    } catch {
      setMessage('We could not accept this invitation. Please try again.');
    } finally {
      setAccepting(false);
    }
  };

  const continueToAuth = (mode: 'login' | 'signup') => {
    if (!token) return;
    storeInvitationToken(token);
    navigate(`/${mode}?invite=${encodeURIComponent(token)}`, { state: { invitationPreview: preview } });
  };

  const signOutAndContinue = async () => {
    await logout();
    if (token) continueToAuth('login');
  };

  return (
    <main className="min-h-screen flex items-center justify-center p-4 bg-[var(--bg-canvas)] text-[var(--text-main)]">
      <section className="w-full max-w-lg p-6 sm:p-8 rounded-3xl bg-[var(--bg-surface)] shadow-2xl space-y-5">
        <div className="w-12 h-12 rounded-2xl flex items-center justify-center bg-[var(--brand-subtle)] text-[var(--brand-primary)]">
          <Users aria-hidden="true" />
        </div>
        {state === 'loading' ? (
          <div role="status" className="flex items-center gap-3"><Loader2 className="animate-spin" />Loading invitation...</div>
        ) : state === 'invalid' ? (
          <>
            <h1 className="text-2xl font-bold">Invitation unavailable</h1>
            <p role="alert">This invitation link is invalid or has been revoked.</p>
          </>
        ) : state === 'expired' ? (
          <>
            <h1 className="text-2xl font-bold">Invitation expired</h1>
            <p role="alert">This invitation has expired. Ask the workspace owner or administrator to send a new one.</p>
          </>
        ) : state === 'used' ? (
          <>
            <h1 className="text-2xl font-bold">Invitation already used</h1>
            <p role="status">This invitation has already been accepted.</p>
            {isAuthenticated && <Link className="text-[var(--brand-primary)] underline" to="/select-org">Go to your workspaces</Link>}
          </>
        ) : state === 'error' ? (
          <>
            <h1 className="text-2xl font-bold">Could not load invitation</h1>
            <p role="alert">Please check your connection and open the invitation link again.</p>
          </>
        ) : preview && (
          <>
            <h1 className="text-2xl font-bold">You’re invited to {preview.workspaceName}</h1>
            <p className="text-[var(--text-secondary)]">
              {preview.inviterName} invited you as a <strong>{preview.role}</strong>.
            </p>
            <p className="flex items-center gap-2 text-sm text-[var(--text-muted)]">
              <Mail className="w-4 h-4" aria-hidden="true" />Invitation sent to {preview.emailMasked}
            </p>
            {message && <div role="alert" className="p-3 rounded-xl bg-rose-500/10 text-rose-400">{message}</div>}
            {!isAuthenticated ? (
              <div className="flex flex-wrap gap-3">
                <button type="button" className="btn-primary" onClick={() => continueToAuth('login')}>Sign in</button>
                <button type="button" className="px-4 py-2 rounded-xl border border-[var(--border-subtle)]" onClick={() => continueToAuth('signup')}>Create account</button>
              </div>
            ) : matchingEmail ? (
              <button type="button" className="btn-primary" disabled={accepting} onClick={() => void accept()}>
                {accepting ? 'Accepting...' : 'Accept invitation'}
              </button>
            ) : (
              <div className="space-y-3">
                <p role="status">The signed-in email does not match the invitation’s masked address. Sign out and continue with the invited email.</p>
                <button type="button" className="btn-primary" onClick={() => void signOutAndContinue()}>Sign out and continue</button>
              </div>
            )}
          </>
        )}
        {message && state !== 'ready' && <p role="alert" className="flex items-center gap-2 text-rose-400"><AlertCircle aria-hidden="true" />{message}</p>}
      </section>
    </main>
  );
};

export default Invitation;
