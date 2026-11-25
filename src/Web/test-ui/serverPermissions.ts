import { existsSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';

// The server's permission list. If this file moves, update the path here so every check built on it keeps running.
// Resolved from this folder up to the repository root (test-ui → Web → src → repo).
const REPO_ROOT = resolve(__dirname, '../../..');
export const SYSTEM_PERMISSIONS_FILE = resolve(REPO_ROOT, 'src/Core/Domain/Security/SystemModule.cs');

/** Every permission key the server defines (module keys such as "sales" have no dot and are not permissions). */
export function serverPermissions(): string[] {
  if (!existsSync(SYSTEM_PERMISSIONS_FILE)) {
    throw new Error(`The server permission list was not found at ${SYSTEM_PERMISSIONS_FILE}. Update SYSTEM_PERMISSIONS_FILE in test-ui/serverPermissions.ts to where SystemPermissions now lives.`);
  }
  const source = readFileSync(SYSTEM_PERMISSIONS_FILE, 'utf8');
  const permissions = [...source.matchAll(/public const string \w+ = "([a-z_]+(?:\.[a-z_]+)+)";/g)].map((m) => m[1]);
  return [...new Set(permissions)];
}
