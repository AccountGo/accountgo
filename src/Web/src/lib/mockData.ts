import { OrgDetails, UserProfile } from '../types';

export const MOCK_USER: UserProfile = {
  id: 'usr_admin',
  name: 'Admin User',
  email: 'admin@accountgo.ph',
  role: 'System Administrator',
};

export const MOCK_ORGS: OrgDetails[] = [
  {
    id: 'org_acme_01',
    name: 'Acme Corporation',
    slug: 'acme-corp',
    role: 'Owner',
    plan: 'enterprise',
    currency: 'USD',
    fiscalYearEnd: 'Dec 31',
    avatarBg: 'bg-gradient-to-br from-indigo-500 to-blue-600',
    whitelabel: {
      themeId: 'sapphire',
      brandName: 'Acme Financials',
    },
  },
  {
    id: 'org_ph_02',
    name: 'Philippine Financial Services',
    slug: 'ph-financials',
    role: 'Admin',
    plan: 'growth',
    currency: 'PHP',
    fiscalYearEnd: 'Dec 31',
    avatarBg: 'bg-gradient-to-br from-emerald-500 to-teal-600',
    whitelabel: {
      themeId: 'accountgo',
      brandName: 'AccountGo PH',
    },
  },
  {
    id: 'org_stark_03',
    name: 'Stark Industries Ltd.',
    slug: 'stark-industries',
    role: 'Member',
    plan: 'enterprise',
    currency: 'USD',
    fiscalYearEnd: 'Jun 30',
    avatarBg: 'bg-gradient-to-br from-amber-500 to-rose-600',
    whitelabel: {
      themeId: 'amber',
      brandName: 'Stark Enterprise Core',
    },
  },
  {
    id: 'org_nexus_04',
    name: 'Nexus Holdings Group',
    slug: 'nexus-holdings',
    role: 'Viewer',
    plan: 'starter',
    currency: 'EUR',
    fiscalYearEnd: 'Dec 31',
    avatarBg: 'bg-gradient-to-br from-purple-500 to-fuchsia-600',
    whitelabel: {
      themeId: 'tokyo-night',
      brandName: 'Nexus Global Accounts',
    },
  },
];

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
