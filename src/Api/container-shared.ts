import {
  BuildMismatchError,
  StartupInterruptedError,
  errorText,
  isStartupTransient,
  isTransientContainerError,
  serviceUnavailable,
} from "./container-errors";

export function sleep(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

/** Prefer INTERNAL_SYNC_KEY so JWT signing material is not the internal build-check credential. */
export function resolveInternalSyncKey(env: {
  INTERNAL_SYNC_KEY?: string;
  SECRET_KEY?: string;
  SECRET__key?: string;
}): string | undefined {
  return env.INTERNAL_SYNC_KEY || env.SECRET_KEY || env.SECRET__key || undefined;
}

export {
  ContainerConfigError,
  buildContainerEnvVars,
  requirePersistentDatabase,
} from "./container-env";

export {
  BuildMismatchError,
  StartupInterruptedError,
  errorText,
  isStartupTransient,
  isTransientContainerError,
  serviceUnavailable,
};
