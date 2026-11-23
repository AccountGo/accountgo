// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

import { useEffect, useState } from 'react';

/**
 * Resolves a tax's public id to a short label ("VAT 8.25%") for a linked capture's persisted tax treatment. Fetches the
 * workspace taxes once, and only when a capture actually carries a rate; a failed fetch just leaves the label off.
 */
export function useTaxLabels(
  authFetch: (input: string, init?: RequestInit) => Promise<Response>,
  needed: boolean
): (taxPublicId: string) => string | null {
  const [labels, setLabels] = useState<Record<string, string>>({});
  useEffect(() => {
    if (!needed) return;
    let cancelled = false;
    (async () => {
      try {
        const res = await authFetch('/api/Tax/Taxes');
        if (!res.ok || cancelled) return;
        const data = await res.json();
        const next: Record<string, string> = {};
        for (const t of (data?.taxes ?? []) as Array<{ id: string; taxName: string; rate: number }>) {
          next[t.id] = `${t.taxName} ${t.rate}%`;
        }
        if (!cancelled) setLabels(next);
      } catch {
        /* best effort */
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [authFetch, needed]);
  return (taxPublicId) => labels[taxPublicId] ?? null;
}
