import { ThemeId, SchemeId } from '../themes';

export interface WhitelabelConfig {
  themeId?: ThemeId;
  schemeId?: SchemeId;
  brandName?: string;
  logoUrl?: string;
  primaryColor?: string;
}

export interface OrgDetails {
  id: string;
  name: string;
  slug: string;
  role?: string;
  plan?: 'starter' | 'growth' | 'enterprise';
  currency?: string;
  fiscalYearEnd?: string;
  avatarBg?: string;
  whitelabel?: WhitelabelConfig;
}

export interface UserProfile {
  id: string;
  name: string;
  email: string;
  role: string;
  avatarUrl?: string;
}

export type SidebarMode = 'expanded' | 'collapsed' | 'closed';

export interface TenantContextType {
  currentOrg: OrgDetails;
  availableOrgs: OrgDetails[];
  user: UserProfile;
  switchOrg: (slug: string) => void;
  updateOrgWhitelabel?: (config: WhitelabelConfig) => void;
  sidebarMode?: SidebarMode;
  setSidebarMode?: (mode: SidebarMode) => void;
}
