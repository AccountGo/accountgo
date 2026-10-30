// Kept free of imports so the node tests can load it directly.

/** The Worker's configuration cannot run the container safely (see requirePersistentDatabase). */
export class ContainerConfigError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "ContainerConfigError";
  }
}

/**
 * The container's disk is thrown away on every restart and deploy, so its data must live in Postgres. Refuse to start
 * with any other provider instead of serving from an empty SQLite file.
 */
export function requirePersistentDatabase(env: { DatabaseProvider?: unknown }): void {
  const provider = typeof env.DatabaseProvider === "string" ? env.DatabaseProvider.trim() : "";
  if (!/^postgres(ql)?$/i.test(provider)) {
    throw new ContainerConfigError(
      `DatabaseProvider must be Postgres on Cloudflare (got "${provider || "unset"}"): container storage is not persistent.`
    );
  }
}

export function buildContainerEnvVars(
  env: Record<string, unknown>,
  containerPort: number
): Record<string, string> {
  const envVars: Record<string, string> = {
    ASPNETCORE_URLS: `http://+:${containerPort}`,
    ASPNETCORE_ENVIRONMENT: "Production",
  };
  for (const [key, value] of Object.entries(env)) {
    if (typeof value === "string") {
      envVars[key] = value;
    }
  }
  return envVars;
}
