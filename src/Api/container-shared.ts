import {
  BuildMismatchError,
  RestoreError,
  RestoreInterruptedError,
  errorText,
  isRestoreTransient,
  isTransientContainerError,
  serviceUnavailable,
} from "./container-errors";
import { scrubPathForLog } from "./server-timing";

export function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

/** Prefer INTERNAL_SYNC_KEY so JWT signing material is not the DB restore credential. */
export function resolveInternalSyncKey(env: {
  INTERNAL_SYNC_KEY?: string;
  SECRET_KEY?: string;
  SECRET__key?: string;
}): string | undefined {
  return env.INTERNAL_SYNC_KEY || env.SECRET_KEY || env.SECRET__key || undefined;
}

/** JSON-safe cell values for structured restore (no SQL string embedding). */
export function normalizeD1Cell(value: unknown): unknown {
  if (value === null || value === undefined) return null;
  if (typeof value === "string" || typeof value === "number" || typeof value === "boolean") {
    return value;
  }
  if (typeof ArrayBuffer !== "undefined" && value instanceof ArrayBuffer) {
    const bytes = new Uint8Array(value);
    let binary = "";
    for (let i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]!);
    return { $b64: btoa(binary) };
  }
  if (typeof Uint8Array !== "undefined" && value instanceof Uint8Array) {
    let binary = "";
    for (let i = 0; i < value.length; i++) binary += String.fromCharCode(value[i]!);
    return { $b64: btoa(binary) };
  }
  return String(value);
}

export { buildContainerEnvVars, usesD1Copy } from "./container-env";

export type ContainerFetchWithRetry = (
  input: string | Request,
  init: RequestInit | undefined,
  label: string
) => Promise<Response>;

/**
 * Full D1 → container SQLite restore used by V1 and V2 writers.
 */
export async function restoreD1ToContainer(options: {
  db: D1Database | undefined;
  env: {
    INTERNAL_SYNC_KEY?: string;
    SECRET_KEY?: string;
    SECRET__key?: string;
  };
  containerFetchWithRetry: ContainerFetchWithRetry;
}): Promise<void> {
  const { db, env, containerFetchWithRetry } = options;
  if (!db) return;
  try {
    const tableRes = await db
      .prepare(
        "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '_cf_%' AND name != 'd1_migrations' AND name != '__SchemaVersion'"
      )
      .all();

    const syncTables = (tableRes.results || []).map((r: { name: string }) => r.name as string);

    type RestoreRow = { table: string; data: Record<string, unknown> };
    const rows: RestoreRow[] = [];

    for (const table of syncTables) {
      try {
        const res = await db.prepare(`SELECT * FROM "${table}"`).all();
        if (res.results && res.results.length > 0) {
          for (const row of res.results) {
            const data: Record<string, unknown> = {};
            for (const [key, value] of Object.entries(row as Record<string, unknown>)) {
              data[key] = normalizeD1Cell(value);
            }
            rows.push({ table, data });
          }
        }
      } catch {
        // Table might not exist or be unreadable
      }
    }

    const syncKey = resolveInternalSyncKey(env);
    if (!syncKey) {
      console.error(
        "Cold start D1 restore skipped: INTERNAL_SYNC_KEY (or SECRET_KEY fallback) is not configured."
      );
      throw new RestoreError(
        "Database restore skipped: INTERNAL_SYNC_KEY (or SECRET_KEY) is not configured on the Worker."
      );
    }

    const batchSize = 200;
    if (rows.length === 0) {
      if (syncTables.length > 0) {
        const res = await containerFetchWithRetry(
          "http://127.0.0.1:8001/api/internal/d1-restore",
          {
            method: "POST",
            headers: {
              "content-type": "application/json",
              "x-internal-key": syncKey,
            },
            body: JSON.stringify({ tablesToClear: syncTables, rows: [] }),
          },
          "d1-restore-clear"
        );
        if (!res.ok) {
          const errText = await res.text();
          throw new Error(`Clear tables failed (${res.status}): ${errText}`);
        }
      }
      return;
    }

    for (let i = 0; i < rows.length; i += batchSize) {
      const batch = rows.slice(i, i + batchSize);
      const isFirstBatch = i === 0;
      const res = await containerFetchWithRetry(
        "http://127.0.0.1:8001/api/internal/d1-restore",
        {
          method: "POST",
          headers: {
            "content-type": "application/json",
            "x-internal-key": syncKey,
          },
          body: JSON.stringify({
            tablesToClear: isFirstBatch ? syncTables : undefined,
            rows: batch,
          }),
        },
        `d1-restore-batch-${i / batchSize}`
      );
      if (!res.ok) {
        const errText = await res.text();
        throw new Error(`Restore batch ${i / batchSize} failed (${res.status}): ${errText}`);
      }
      const result = (await res.json()) as { restored?: number };
      if (result.restored !== batch.length) {
        throw new Error(`Batch count mismatch: restored ${result.restored} of ${batch.length}`);
      }
    }
  } catch (err) {
    console.error("Cold start D1 restore failed:", err);
    if (err instanceof RestoreError) throw err;
    throw new RestoreError(
      `Database restore from Cloudflare D1 failed: ${err instanceof Error ? err.message : String(err)}`
    );
  }
}

export async function applyD1SyncFromResponse(options: {
  db: D1Database | undefined;
  syncStatements: string[];
  method: string;
  pathname: string;
}): Promise<void> {
  const { db, syncStatements, method, pathname } = options;
  if (syncStatements.length === 0 || !db) return;
  try {
    const prepared = [
      db.prepare("PRAGMA defer_foreign_keys = on"),
      ...syncStatements.map((s) => db.prepare(s)),
    ];
    await db.batch(prepared);
  } catch (syncErr: unknown) {
    const message = syncErr instanceof Error ? syncErr.message : String(syncErr);
    const scrubbedPath = scrubPathForLog(pathname);
    console.error(
      `[worker] D1 sync batch failed: method=${method} path=${scrubbedPath} error=${message} statementCount=${syncStatements.length}`
    );
    throw syncErr;
  }
}

export {
  BuildMismatchError,
  RestoreError,
  RestoreInterruptedError,
  errorText,
  isRestoreTransient,
  isTransientContainerError,
  serviceUnavailable,
};
