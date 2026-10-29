type AuthFetch = (url: string, init?: RequestInit) => Promise<Response>;

let authFetchImpl: AuthFetch | null = null;

/** Wire once from AuthProvider so the module-level adapter can call authenticated APIs. */
export function setAssistantAuthFetch(authFetch: AuthFetch): void {
  authFetchImpl = authFetch;
}

export async function assistantFetch(path: string, init?: RequestInit): Promise<Response> {
  if (!authFetchImpl) {
    throw new Error('AccountGo Assistant: auth fetch is not ready');
  }
  const url = path.startsWith('/api/') ? path : `/api${path.startsWith('/') ? path : `/${path}`}`;
  return authFetchImpl(url, init);
}

export async function assistantFetchJson<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await assistantFetch(path, init);
  if (!res.ok) {
    let detail = `HTTP ${res.status}`;
    try {
      const body = (await res.json()) as { detail?: string; title?: string; message?: string };
      detail = body.detail || body.message || body.title || detail;
    } catch {
      /* keep status */
    }
    throw new Error(detail);
  }
  if (res.status === 204) {
    return undefined as T;
  }
  return (await res.json()) as T;
}

export async function assistantFetchStream(path: string, init?: RequestInit): Promise<ReadableStream<Uint8Array>> {
  const res = await assistantFetch(path, {
    ...init,
    headers: {
      Accept: 'text/event-stream',
      ...(init?.headers || {}),
    },
  });
  if (!res.ok) {
    let detail = `HTTP ${res.status}`;
    try {
      const body = (await res.json()) as { detail?: string; title?: string; message?: string };
      detail = body.detail || body.message || body.title || detail;
    } catch {
      /* keep status */
    }
    throw new Error(detail);
  }
  if (!res.body) {
    throw new Error('AccountGo Assistant: missing response stream');
  }
  return res.body;
}
