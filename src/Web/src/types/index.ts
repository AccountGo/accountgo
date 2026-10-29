import { ThemeId, SchemeId } from '../themes';

export type ModuleKey = 
  | 'sales' 
  | 'financials' 
  | 'purchases' 
  | 'inventory' 
  | 'reports' 
  | 'donations' 
  | 'audit';

/** Plan key from the server catalog / entitlements snapshot (no client-side union). */
export type PlanKey = string;

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
  monthlyPrice: number | null;
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
  permissions?: string[];
  fiscalYearEnd?: string;
  avatarBg?: string;
  whitelabel?: WhitelabelConfig;
  subscription?: SubscriptionInfo;
}

export interface Organization {
  id: string;
  name: string;
  shortName?: string;
  slug: string;
  plan: PlanKey;
  subscriptionStatus: SubscriptionInfo['status'];
  activeModules: ModuleKey[];
  role: WorkspaceRole;
  currency?: string;
  permissions?: string[];
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

export interface PendingInvitation {
  id: number;
  workspaceName: string;
  slug: string;
  role: Exclude<WorkspaceRole, 'Owner'>;
  expiresAt: string;
}

export interface InvitationPreview {
  workspaceName: string;
  inviterName: string;
  role: Exclude<WorkspaceRole, 'Owner'>;
  emailMasked: string;
  expired: boolean;
  accepted: boolean;
}

export interface TenantContextType {
  currentOrg: OrgDetails;
  availableOrgs: OrgDetails[];
  user: UserProfile;
  subscription: SubscriptionInfo;
  userPermissions: string[];
  switchOrg: (slug: string) => void;
  updateCurrentOrg: (updates: Partial<OrgDetails>) => void;
  updateOrgWhitelabel?: (config: WhitelabelConfig) => void;
  updateSubscription?: (
    plan: PlanKey,
    activeModules?: ModuleKey[],
    options?: { billingInterval?: 'month' | 'year' },
  ) => Promise<{ redirectUrl?: string } | void>;
  hasModule: (module: ModuleKey) => boolean;
  /** Write path for a module (false when the module is read-only). */
  canWrite: (module: ModuleKey) => boolean;
  hasPermission: (permission: string | string[]) => boolean;
  canAccess: (module: ModuleKey, permission?: string) => boolean;
  sidebarMode?: SidebarMode;
  setSidebarMode?: (mode: SidebarMode) => void;
}

export * from './invoicing';
export * from './parties';
export * from './purchasing';
