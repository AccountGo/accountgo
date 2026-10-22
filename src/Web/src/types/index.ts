import { ThemeId, SchemeId } from '../themes';

export type ModuleKey = 
  | 'sales' 
  | 'financials' 
  | 'purchases' 
  | 'inventory' 
  | 'reports' 
  | 'donations' 
  | 'audit';

export type PlanKey = 'invoicing' | 'accounting' | 'enterprise';

export interface SubscriptionInfo {
  planId: PlanKey;
  planName: string;
  status: 'active' | 'trialing' | 'past_due' | 'canceled';
  activeModules: ModuleKey[];
  validUntil?: string;
}

export interface SubscriptionPlanDefinition {
  key: PlanKey;
  name: string;
  description: string;
  includedModules: ModuleKey[];
  monthlyPrice: number;
}

export interface ModuleDefinition {
  key: ModuleKey;
  name: string;
  description: string;
  icon: string;
  isCore: boolean;
}

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
  shortName?: string;
  companyCode?: string;
  cra?: string;
  role?: string;
  plan?: PlanKey;
  currency?: string;
  fiscalYearEnd?: string;
  avatarBg?: string;
  whitelabel?: WhitelabelConfig;
  subscription?: SubscriptionInfo;
}

export interface UserProfile {
  id: string;
  name: string;
  email: string;
  role: string;
  avatarUrl?: string;
  permissions?: string[];
}

export type SidebarMode = 'expanded' | 'collapsed' | 'closed';

export interface RolePermissionItem {
  id: number;
  name: string;
  displayName?: string;
}

export interface RoleItem {
  id: number;
  name: string;
  displayName: string;
  sysAdmin?: boolean;
  permissions?: RolePermissionItem[];
}

export interface TeamMember {
  id: number;
  firstName?: string;
  lastName?: string;
  userName: string;
  email: string;
  sysAdmin?: boolean;
  roles: RoleItem[];
}

export type WorkspaceRole = 'Owner' | 'Admin' | 'Member';

export interface WorkspaceMember {
  memberId: number;
  userId: string;
  email: string;
  firstName: string;
  lastName: string;
  role: WorkspaceRole;
  securityRoles: RoleItem[];
  status: 'Active' | 'Suspended';
  joinedAt: string;
}

export interface WorkspaceInvitation {
  id: number;
  email: string;
  role: Exclude<WorkspaceRole, 'Owner'>;
  securityRoleIds: number[];
  expiresAt: string;
}

export interface TenantContextType {
  currentOrg: OrgDetails;
  availableOrgs: OrgDetails[];
  user: UserProfile;
  subscription: SubscriptionInfo;
  userPermissions: string[];
  switchOrg: (slug: string) => void;
  updateOrgWhitelabel?: (config: WhitelabelConfig) => void;
  updateSubscription?: (plan: PlanKey, activeModules?: ModuleKey[]) => Promise<void>;
  hasModule: (module: ModuleKey) => boolean;
  hasPermission: (permission: string | string[]) => boolean;
  canAccess: (module: ModuleKey, permission?: string) => boolean;
  sidebarMode?: SidebarMode;
  setSidebarMode?: (mode: SidebarMode) => void;
}

export * from './invoicing';
