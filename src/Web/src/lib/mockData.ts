import { OrgDetails, UserProfile } from '../types';

export const MOCK_USER: UserProfile = {
  id: '',
  name: '',
  email: '',
  role: 'Member',
  permissions: [],
};

export const MOCK_ORGS: OrgDetails[] = [];

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

