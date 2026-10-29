#!/usr/bin/env node
/**
 * Unauthenticated Server-Timing latency bench (no new dependencies).
 * Usage: node scripts/latency-bench.mjs [N] [url ...]
 * Never calls /api/auth/*.
 */
import { performance } from "node:perf_hooks";

const DEFAULT_URLS = [
  "https://api.accountgo.dev/health",
  "https://app.accountgo.dev/api/Subscription/plans",
];

function parseArgs(argv) {
  const rest = argv.slice(2);
  let n = 20;
  let urls = [];
  if (rest.length > 0 && /^\d+$/.test(rest[0])) {
    n = Number(rest[0]);
    urls = rest.slice(1);
  } else {
    urls = rest;
  }
  if (urls.length === 0) urls = DEFAULT_URLS;
  for (const u of urls) {
    if (/\/api\/auth(\/|$)/i.test(u)) {
      throw new Error(`Refusing to bench auth URL: ${u}`);
    }
  }
  return { n, urls };
}

function parseServerTiming(header) {
  const out = {};
  if (!header) return out;
  for (const part of header.split(",")) {
    const bits = part.trim().split(";");
    const name = bits[0]?.trim();
    if (!name) continue;
    let dur;
    for (const b of bits.slice(1)) {
      const m = /^dur=([\d.]+)$/i.exec(b.trim());
      if (m) dur = Number(m[1]);
    }
    if (dur !== undefined && Number.isFinite(dur)) out[name] = dur;
  }
  return out;
}

function percentile(sorted, p) {
  if (sorted.length === 0) return null;
  const idx = Math.min(sorted.length - 1, Math.ceil((p / 100) * sorted.length) - 1);
  return sorted[Math.max(0, idx)];
}

async function sample(url) {
  const t0 = performance.now();
  const res = await fetch(url, { method: "GET", redirect: "follow" });
  const ttfb = performance.now() - t0;
  // Headers are available at TTFB; do not require full body for timing parse.
  const timing = parseServerTiming(res.headers.get("server-timing"));
  // Drain body so keep-alive stays healthy, but TTFB already recorded.
  await res.arrayBuffer().catch(() => null);
  return { status: res.status, ttfb, timing };
}

function summarize(label, values) {
  const sorted = [...values].filter((v) => Number.isFinite(v)).sort((a, b) => a - b);
  if (sorted.length === 0) {
    console.log(`  ${label}: (no samples)`);
    return;
  }
  console.log(
    `  ${label}: p50=${percentile(sorted, 50)?.toFixed(1)}ms  p95=${percentile(sorted, 95)?.toFixed(1)}ms  n=${sorted.length}`
  );
}

const { n, urls } = parseArgs(process.argv);
console.log(`latency-bench: N=${n}`);
for (const url of urls) {
  console.log(`\n${url}`);
  const ttfbs = [];
  const bySegment = new Map();
  for (let i = 0; i < n; i++) {
    try {
      const s = await sample(url);
      ttfbs.push(s.ttfb);
      for (const [k, v] of Object.entries(s.timing)) {
        if (!bySegment.has(k)) bySegment.set(k, []);
        bySegment.get(k).push(v);
      }
      if (s.status >= 400) {
        console.warn(`  sample ${i + 1}: HTTP ${s.status}`);
      }
    } catch (err) {
      console.warn(`  sample ${i + 1}: ${err instanceof Error ? err.message : String(err)}`);
    }
  }
  summarize("client_ttfb", ttfbs);
  for (const [name, values] of [...bySegment.entries()].sort((a, b) => a[0].localeCompare(b[0]))) {
    summarize(name, values);
  }
}
