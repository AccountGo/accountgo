/**
 * Worker / Durable Object env. Shared by V1 and V2 without importing class modules
 * (avoids circular imports).
 */
export interface Env {
  CONTAINER: DurableObjectNamespace;
  CONTAINER_V2?: DurableObjectNamespace;
  ENVIRONMENT?: string;
  CONTAINER_NAME?: string;
  /** Phase 0: when "true", route to CONTAINER_V2 (still one shared writer). */
  TIERED_CAPACITY_ENABLED?: string;
  /** Phase 0 V2 start() instance: lite | standard-1 … standard-4. */
  CONTAINER_INSTANCE_TYPE?: string;
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
