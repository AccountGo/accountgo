// Kept free of imports so the node tests can load it directly.

/**
 * True when the container keeps its data in container SQLite, restored from D1 and synced back after each write.
 * With `DatabaseProvider = "Postgres"` the container talks to Postgres itself: no restore and no sync.
 */
export function usesD1Copy(env: { DatabaseProvider?: unknown }): boolean {
  const provider = typeof env.DatabaseProvider === "string" ? env.DatabaseProvider.trim() : "";
  return !/^postgres(ql)?$/i.test(provider);
}

export function buildContainerEnvVars(
  env: Record<string, unknown>,
  containerPort: number
): Record<string, string> {
  const envVars: Record<string, string> = {
    ASPNETCORE_URLS: `http://+:${containerPort}`,
    ASPNETCORE_ENVIRONMENT: "Production",
    DatabaseProvider: "Sqlite",
  };
  if (usesD1Copy(env)) {
    // The data is restored from D1 once the container is up, so it must not seed its own demo data first.
    envVars.RESTORE_FROM_D1 = "true";
  }
  for (const [key, value] of Object.entries(env)) {
    if (typeof value === "string") {
      envVars[key] = value;
    }
  }
  return envVars;
}
