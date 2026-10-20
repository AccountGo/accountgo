export interface Env {
  ASSETS: Fetcher;
  API?: Fetcher;
  API_HOST?: string;
}

export default {
  async fetch(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
    const url = new URL(request.url);

    // Forward API requests to the backend worker or API host
    if (url.pathname.startsWith('/api/')) {
      if (env.API) {
        return env.API.fetch(request);
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

        return fetch(new Request(proxyUrl, {
          method: request.method,
          headers: proxyHeaders,
          body: request.body,
          redirect: 'manual',
        }));
      }

      return new Response(JSON.stringify({ error: 'API service binding not configured' }), {
        status: 503,
        headers: { 'Content-Type': 'application/json' },
      });
    }

    // Fall back to static assets (SPA router fallback handles client routes)
    return env.ASSETS.fetch(request);
  },
};
