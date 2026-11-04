export interface Env {
  ASSETS: Fetcher;
  API?: Fetcher;
  API_HOST?: string;
  /** Observability: emit Server-Timing (default on when unset). */
  SERVER_TIMING_ENABLED?: string;
}

function isServerTimingEnabled(env: Env): boolean {
  const raw = env.SERVER_TIMING_ENABLED;
  if (raw === undefined || raw === null || String(raw).trim() === "") return true;
  switch (String(raw).trim().toLowerCase()) {
    case "0":
    case "false":
    case "no":
    case "off":
      return false;
    default:
      return true;
  }
}

function appendProxyTiming(response: Response, proxyMs: number, enabled: boolean): Response {
  if (!enabled || response.status === 101) return response;
  const headers = new Headers(response.headers);
  const existing = headers.get("Server-Timing");
  const segment = `proxy;dur=${Math.max(0, Math.round(proxyMs))}`;
  headers.set("Server-Timing", existing && existing.trim() ? `${existing.trim()}, ${segment}` : segment);
  // Wrap body without reading — preserves SSE/streaming.
  return new Response(response.body, {
    status: response.status,
    statusText: response.statusText,
    headers,
  });
}

export default {
  async fetch(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
    const url = new URL(request.url);
    const timingEnabled = isServerTimingEnabled(env);

    // Forward API requests to the backend worker or API host
    if (url.pathname.startsWith('/api/')) {
      if (env.API) {
        const started = Date.now();
        const res = await env.API.fetch(request);
        return appendProxyTiming(res, Date.now() - started, timingEnabled);
      }
      if (env.API_HOST) {
        const proxyUrl = new URL(request.url);
        const [host, port] = env.API_HOST.split(':');
        proxyUrl.host = host;
        if (port) {
          proxyUrl.port = port;
        } else {
          proxyUrl.port = '';
        }
        proxyUrl.protocol = env.API_HOST.startsWith('localhost') || env.API_HOST.startsWith('127.0.0.1') ? 'http:' : 'https:';

        const proxyHeaders = new Headers(request.headers);
        proxyHeaders.set('X-Forwarded-Host', url.host);
        proxyHeaders.set('X-Forwarded-Proto', url.protocol.replace(':', ''));

        const started = Date.now();
        const res = await fetch(new Request(proxyUrl, {
          method: request.method,
          headers: proxyHeaders,
          body: request.body,
          redirect: 'manual',
        }));
        return appendProxyTiming(res, Date.now() - started, timingEnabled);
      }

      return new Response(JSON.stringify({ error: 'API service binding not configured' }), {
        status: 503,
        headers: { 'Content-Type': 'application/json' },
      });
    }

    // Marketing/search indexing lives on accountgo.dev. Keep the app shell out of SERPs
    // so brand queries are not split between the marketing site and the SPA.
    if (url.pathname === "/robots.txt") {
      return new Response(
        [
          "# AccountGo web app (app.accountgo.dev)",
          "# Marketing and search indexing live on https://accountgo.dev.",
          "# The app shell is intentionally not indexed so brand SERPs are not split.",
          "User-agent: *",
          "Disallow: /",
          "",
        ].join("\n"),
        {
          headers: {
            "Content-Type": "text/plain; charset=utf-8",
            "X-Robots-Tag": "noindex",
            "Cache-Control": "public, max-age=3600",
          },
        },
      );
    }

    // Fall back to static assets (SPA router fallback handles client routes)
    const assetResponse = await env.ASSETS.fetch(request);
    const headers = new Headers(assetResponse.headers);
    if (!headers.has("X-Robots-Tag")) {
      headers.set("X-Robots-Tag", "noindex, nofollow");
    }
    return new Response(assetResponse.body, {
      status: assetResponse.status,
      statusText: assetResponse.statusText,
      headers,
    });
  },
};
