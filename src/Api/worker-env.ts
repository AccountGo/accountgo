/**
 * Worker / Durable Object env. Shared by V1 and V2 without importing class modules
 * (avoids circular imports).
 */
export interface Env {
  CONTAINER: DurableObjectNamespace;
  CONTAINER_V2?: DurableObjectNamespace;
  ENVIRONMENT?: string;
  /** Must be "Postgres" on Cloudflare (requirePersistentDatabase). */
  DatabaseProvider?: string;
  CONTAINER_NAME?: string;
  /** Phase 0: when "true", route to CONTAINER_V2 (still one shared writer). */
  TIERED_CAPACITY_ENABLED?: string;
  /** V2 start() instance for the paid pool (default standard-1) and the free pool (default lite). */
  CONTAINER_INSTANCE_TYPE_PAID?: string;
  CONTAINER_INSTANCE_TYPE_FREE?: string;
  /** Dedicated key for container-loopback internal sync; prefer over SECRET_KEY. */
  INTERNAL_SYNC_KEY?: string;
  SECRET_KEY?: string;
  SECRET__key?: string;
  BUILD_SHA?: string;
  TURNSTILE_SECRET?: string;
  TURNSTILE_HOSTNAMES?: string;
  /** Observability: emit Server-Timing (default on when unset). */
  SERVER_TIMING_ENABLED?: string;
  CF_VERSION_METADATA?: {
    id: string;
    tag: string;
  };
}
