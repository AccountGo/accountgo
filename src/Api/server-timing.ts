/** Shared Server-Timing helpers for the API worker / Durable Object (observability only). */

export const SERVER_TIMING_HEADER = "Server-Timing";
export const WORKER_SLOW_THRESHOLD_MS = 1500;

export type TimingSegment = {
  name: string;
  dur?: number;
  desc?: string;
};

export function isServerTimingEnabled(env: { SERVER_TIMING_ENABLED?: string } | undefined): boolean {
  const raw = env?.SERVER_TIMING_ENABLED;
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

export function formatServerTiming(segments: TimingSegment[]): string {
  return segments
    .map((s) => {
      let part = s.name;
      if (s.dur !== undefined && Number.isFinite(s.dur)) {
        part += `;dur=${Math.max(0, Math.round(s.dur))}`;
      }
      if (s.desc !== undefined && s.desc !== "") {
        part += `;desc="${String(s.desc).replace(/"/g, "")}"`;
      }
      return part;
    })
    .join(", ");
}

/** Append segments to an existing Server-Timing header value (never overwrite). */
export function appendServerTimingValue(existing: string | null, segments: TimingSegment[]): string {
  const next = formatServerTiming(segments);
  if (!next) return existing ?? "";
  if (!existing || !existing.trim()) return next;
  return `${existing.trim()}, ${next}`;
}

/**
 * Wrap a response to add/append Server-Timing without reading the body.
 * Passes 101 Switching Protocols through untouched.
 */
export function withServerTiming(
  response: Response,
  segments: TimingSegment[],
  enabled: boolean
): Response {
  if (!enabled || response.status === 101) return response;
  const headers = new Headers(response.headers);
  const merged = appendServerTimingValue(headers.get(SERVER_TIMING_HEADER), segments);
  if (merged) headers.set(SERVER_TIMING_HEADER, merged);
  return new Response(response.body, {
    status: response.status,
    statusText: response.statusText,
    headers,
  });
}

/**
 * Scrub a request path for logs.
 * - /invitations/{token}[/…] → {token}
 * - Numeric / GUID / hex-hash → {id}
 * - High-entropy base64url-like (≥16 [A-Za-z0-9_-], letter+digit) → {token}
 * - /org/{slug}/ → {org}
 * Never includes query string.
 */
export function scrubPathForLog(pathname: string): string {
  let path = pathname.split("?")[0] || "/";
  if (!path.startsWith("/")) path = `/${path}`;

  const parts = path.split("/").map((segment, index, arr) => {
    if (!segment) return segment;
    // /org/{slug}/...
    if (index > 0 && arr[index - 1] === "org" && !/^\d+$/.test(segment)) {
      return "{org}";
    }
    // /api/invitations/{token} or .../invitations/{token}/accept
    if (index > 0 && arr[index - 1].toLowerCase() === "invitations") {
      return "{token}";
    }
    if (/^\d+$/.test(segment)) return "{id}";
    if (/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(segment)) {
      return "{id}";
    }
    if (/^[0-9a-f]{32,}$/i.test(segment)) return "{id}";
    // Generic high-entropy catch (invitation tokens, opaque ids) — not normal route words.
    if (
      segment.length >= 16 &&
      /^[A-Za-z0-9_-]+$/.test(segment) &&
      /[A-Za-z]/.test(segment) &&
      /\d/.test(segment)
    ) {
      return "{token}";
    }
    return segment;
  });

  return parts.join("/") || "/";
}

export function logSlowWorkerRequest(opts: {
  route: string;
  status: number;
  segments: TimingSegment[];
  workerMs: number;
}): void {
  if (opts.workerMs <= WORKER_SLOW_THRESHOLD_MS) return;
  const flat: Record<string, string | number> = {
    route: opts.route,
    status: opts.status,
    worker_ms: Math.round(opts.workerMs),
  };
  for (const s of opts.segments) {
    if (s.dur !== undefined) flat[`${s.name}_ms`] = Math.round(s.dur);
    if (s.desc !== undefined) flat[`${s.name}_desc`] = s.desc;
  }
  console.warn("[slow-request]", JSON.stringify(flat));
}
