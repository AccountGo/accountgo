import { describe, expect, it, vi } from 'vitest';
import { resolvePostAuthDestination } from '../src/pages/loginContinuation';

// A server-state double: workspaces that still need setup report an unclassified operating policy; an
// initialized workspace reports SoloActive. GET /api/organizations returns the user's org list.
function makeAuthFetch(opts: {
  needsSetup?: Record<string, boolean>;
  orgs?: Array<{ slug: string }>;
}) {
  const needsSetup = opts.needsSetup ?? {};
  const orgs = opts.orgs ?? [];
  const calls: string[] = [];
  const authFetch = vi.fn(async (url: string, init?: RequestInit) => {
    calls.push(url);
    if (url === '/api/organizations') {
      return new Response(JSON.stringify(orgs), { status: 200, headers: { 'Content-Type': 'application/json' } });
    }
    if (url === '/api/organizations/current') {
      const slug = new Headers(init?.headers).get('X-AccountGo-Org') ?? '';
      const policy = needsSetup[slug] === false
        ? { operatingPolicy: { collaborationState: 'SoloActive' } }
        : { operatingPolicy: { collaborationState: 'LegacyUnclassified' } };
      return new Response(JSON.stringify(policy), { status: 200, headers: { 'Content-Type': 'application/json' } });
    }
    return new Response(null, { status: 404 });
  });
  return { authFetch, calls };
}

describe('post-auth continuation routing resolves from trusted server state', () => {
  it('signup slug that needs setup routes to continuation', async () => {
    const { authFetch } = makeAuthFetch({ needsSetup: { acme: true } });
    const d = await resolvePostAuthDestination(authFetch, { targetOrgSlug: 'acme' });
    expect(d).toEqual({ kind: 'continue', slug: 'acme' });
  });

  it('reload/sign-in with no in-memory slug still continues via the persisted slug', async () => {
    const { authFetch } = makeAuthFetch({ needsSetup: { acme: true } });
    // targetOrgSlug/override are gone (reload); only the persisted slug survives.
    const d = await resolvePostAuthDestination(authFetch, { persistedSlug: 'acme' });
    expect(d).toEqual({ kind: 'continue', slug: 'acme' });
  });

  it('original /org/<slug> destination that needs setup routes to continuation', async () => {
    const { authFetch } = makeAuthFetch({ needsSetup: { acme: true } });
    const d = await resolvePostAuthDestination(authFetch, { fromPath: '/org/acme/dashboard' });
    expect(d).toEqual({ kind: 'continue', slug: 'acme' });
  });

  it('sign-in with no slug at all resolves the sole workspace from the server and continues', async () => {
    const { authFetch, calls } = makeAuthFetch({ needsSetup: { acme: true }, orgs: [{ slug: 'acme' }] });
    const d = await resolvePostAuthDestination(authFetch, {});
    expect(d).toEqual({ kind: 'continue', slug: 'acme' });
    // It consulted the org list (trusted server state), not in-memory signup state.
    expect(calls).toContain('/api/organizations');
  });

  it('initialized workspace routes to the dashboard, not continuation', async () => {
    const { authFetch } = makeAuthFetch({ needsSetup: { acme: false } });
    const d = await resolvePostAuthDestination(authFetch, { targetOrgSlug: 'acme' });
    expect(d).toEqual({ kind: 'dashboard', slug: 'acme' });
  });

  it('initialized destination path is followed as-is', async () => {
    const { authFetch } = makeAuthFetch({ needsSetup: { acme: false } });
    const d = await resolvePostAuthDestination(authFetch, { fromPath: '/org/acme/reports' });
    expect(d).toEqual({ kind: 'path', path: '/org/acme/reports' });
  });

  it('no slug and multiple workspaces falls back to the org selector', async () => {
    const { authFetch } = makeAuthFetch({ orgs: [{ slug: 'a' }, { slug: 'b' }] });
    const d = await resolvePostAuthDestination(authFetch, {});
    expect(d).toEqual({ kind: 'select-org' });
  });
});
