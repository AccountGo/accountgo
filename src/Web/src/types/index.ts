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

export type JournalApprovalMode = 'IndependentReviewerRequired' | 'SingleActorAllowed' | 'CatalogDefault';

export interface JournalApprovalSummary {
  mode: JournalApprovalMode | string;
  provenance?: string | null;
  eligibleOtherReviewerCount: number;
  canChange: boolean;
}

export interface WorkspaceOperatingPolicySummary {
  journalApproval?: JournalApprovalSummary | null;
  purpose?: string;
  collaborationIntent?: string;
  collaborationState?: string;
  policyRevision?: number;
  ownAccessProfileKey?: string;
  ownAccessProfileDisplayName?: string;
  journalWorkflow?: string;
  soloSetupEnabled?: boolean;
  allowedNextActions?: string[];
}

export type SoloSetupEligibilityStatus = 'Eligible' | 'Ineligible' | 'RecoveryRequired' | 0 | 1 | 2;

export interface SoloSetupEligibility {
  status: SoloSetupEligibilityStatus;
  reasonCode: string;
  message: string;
  policyRevision: number;
}

export interface WorkspacePolicyMutationResult {
  applied: boolean;
  reasonCode?: string;
  message?: string;
  policyRevision: number;
  collaborationState: string;
}

export interface PersonalBooksMetadata {
  isPersonalBooks?: boolean;
  linkedPersonalCompanyId?: number | null;
  linkedWorkCompanyIds?: number[];
  canAddPersonalBooks?: boolean;
  includedModules?: ModuleKey[];
  requiredModules?: ModuleKey[];
}

export interface OrgDetails extends PersonalBooksMetadata {
  id: string;
  name: string;
  slug: string;
  shortName?: string;
  companyCode?: string;
  cra?: string;
  role?: string;
  plan?: PlanKey;
  currency?: string;
  entityType?: string;
  permissions?: string[];
  fiscalYearEnd?: string;
  avatarBg?: string;
  whitelabel?: WhitelabelConfig;
  subscription?: SubscriptionInfo;
  operatingPolicy?: WorkspaceOperatingPolicySummary | null;
  activationState?: string;
}

export interface Organization extends PersonalBooksMetadata {
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
  activationState?: string;
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
  /** True once the link has expired; the invitation can still be resent (renewed) or revoked. */
  expired?: boolean;
  /** True when the journal approval policy changed since the invitation was issued. */
  policyChanged?: boolean;
}

export interface WorkspaceAccessRequestItem {
  requestId: string;
  requesterUserId: string;
  requesterEmail?: string;
  targetMemberId: number;
  targetUserId: string;
  targetEmail?: string;
  requestedRoleIds: number[];
  requestedRoleNames: string[];
  reason?: string;
  status: 'Pending' | 'Approved' | 'Applied' | 'Rejected' | 'Cancelled' | 'Expired';
  createdAtUtc: string;
  expiresAtUtc: string;
  decidedAtUtc?: string;
  approverUserId?: string;
  approverEmail?: string;
  decisionReason?: string;
  canApprove: boolean;
  canCancel: boolean;
}

export interface AccessSetupStatus {
  alreadyBootstrapped: boolean;
  adminCount: number;
  totalFunctionalRoles: number;
  isEligibleForBootstrap: boolean;
  ownerCanManageOwnAccess?: boolean;
}

export interface PendingInvitation {
  id: number;
  workspaceName: string;
  slug: string;
  role: Exclude<WorkspaceRole, 'Owner'>;
  expiresAt: string;
}

export interface InvitationAccessArea {
  area: string;
  label: string;
  level: string;
}

export interface InvitationAccessSummary {
  roles: string[];
  areas: InvitationAccessArea[];
}

export interface InvitationPreview {
  access?: InvitationAccessSummary;
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
export * from './banking';
