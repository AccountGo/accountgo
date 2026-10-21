const STORAGE_KEY = 'accountgo:lastOrgSlug';

export function getPersistedOrgSlug(): string | null {
  try {
    return localStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}

export function setPersistedOrgSlug(slug: string): void {
  try {
    localStorage.setItem(STORAGE_KEY, slug);
  } catch {
    // Ignore storage errors in restricted contexts
  }
}

export function clearPersistedOrgSlug(): void {
  try {
    localStorage.removeItem(STORAGE_KEY);
  } catch {
    // Ignore storage errors in restricted contexts
  }
}
