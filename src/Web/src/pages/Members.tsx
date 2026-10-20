import React, { useState, useEffect, useMemo, useCallback } from 'react';
import { useOutletContext } from 'react-router-dom';
import {
  Users,
  UserPlus,
  Shield,
  ShieldCheck,
  Search,
  RefreshCw,
  Mail,
  Edit2,
  Trash2,
  Check,
  X,
  AlertTriangle,
  Lock,
  Copy,
  ChevronDown,
} from 'lucide-react';
import { TenantContextType, TeamMember, RoleItem } from '../types';
import { useAuth } from '../auth/AuthContext';

export const Members: React.FC = () => {
  const tenantContext = useOutletContext<TenantContextType>();
  const { currentOrg, user: currentUserProfile } = tenantContext;
  const { authFetch } = useAuth();

  // Active view: 'members' or 'roles'
  const [activeTab, setActiveTab] = useState<'members' | 'roles'>('members');

  // Live database state
  const [members, setMembers] = useState<TeamMember[]>([]);
  const [roles, setRoles] = useState<RoleItem[]>([]);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [isRefreshing, setIsRefreshing] = useState<boolean>(false);
  const [errorBanner, setErrorBanner] = useState<string | null>(null);
  const [successBanner, setSuccessBanner] = useState<string | null>(null);

  // Filters
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedRoleFilter, setSelectedRoleFilter] = useState<string>('all');

  // Modals state
  const [isInviteOpen, setIsInviteOpen] = useState(false);
  const [inviteEmail, setInviteEmail] = useState('');
  const [inviteFirstName, setInviteFirstName] = useState('');
  const [inviteLastName, setInviteLastName] = useState('');
  const [inviteRoleIds, setInviteRoleIds] = useState<number[]>([]);
  const [isSubmittingInvite, setIsSubmittingInvite] = useState(false);
  const [inviteError, setInviteError] = useState<string | null>(null);

  const [editingMember, setEditingMember] = useState<TeamMember | null>(null);
  const [editFirstName, setEditFirstName] = useState('');
  const [editLastName, setEditLastName] = useState('');
  const [editEmail, setEditEmail] = useState('');
  const [editRoleIds, setEditRoleIds] = useState<number[]>([]);
  const [isSubmittingEdit, setIsSubmittingEdit] = useState(false);
  const [editError, setEditError] = useState<string | null>(null);

  const [deletingMember, setDeletingMember] = useState<TeamMember | null>(null);
  const [isSubmittingDelete, setIsSubmittingDelete] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  // Selected role for the Roles & Permissions tab
  const [selectedRoleId, setSelectedRoleId] = useState<number | null>(null);

  // Copy feedback state
  const [copiedEmail, setCopiedEmail] = useState<string | null>(null);

  const showSuccess = (msg: string) => {
    setSuccessBanner(msg);
    setTimeout(() => setSuccessBanner(null), 4000);
  };

  const showError = (msg: string) => {
    setErrorBanner(msg);
    setTimeout(() => setErrorBanner(null), 5000);
  };

  // Fetch live database members and roles
  const loadData = useCallback(async (isSilent = false) => {
    if (!isSilent) setIsLoading(true);
    else setIsRefreshing(true);
    setErrorBanner(null);

    try {
      const [membersRes, rolesRes] = await Promise.all([
        authFetch('/api/Administration/users'),
        authFetch('/api/Administration/roles'),
      ]);

      if (membersRes.ok) {
        const membersData = await membersRes.json();
        if (Array.isArray(membersData)) {
          setMembers(membersData);
        }
      } else {
        const err = await membersRes.text();
        showError(`Failed to load members from database: ${err}`);
      }

      if (rolesRes.ok) {
        const rolesData = await rolesRes.json();
        if (Array.isArray(rolesData)) {
          setRoles(rolesData);
          if (rolesData.length > 0 && selectedRoleId === null) {
            setSelectedRoleId(rolesData[0].id);
          }
        }
      }
    } catch (err: any) {
      showError(`Network error while communicating with backend: ${err?.message || err}`);
    } finally {
      setIsLoading(false);
      setIsRefreshing(false);
    }
  }, [authFetch, selectedRoleId]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  // Filtered members
  const filteredMembers = useMemo(() => {
    return members.filter((m) => {
      const fullName = `${m.firstName || ''} ${m.lastName || ''}`.trim().toLowerCase();
      const email = (m.email || '').toLowerCase();
      const query = searchQuery.toLowerCase().trim();

      const matchesQuery =
        !query ||
        fullName.includes(query) ||
        email.includes(query) ||
        (m.roles || []).some(
          (r) =>
            r.name.toLowerCase().includes(query) ||
            r.displayName.toLowerCase().includes(query)
        );

      const matchesRole =
        selectedRoleFilter === 'all' ||
        (m.roles || []).some(
          (r) => r.id.toString() === selectedRoleFilter || r.name === selectedRoleFilter
        );

      return matchesQuery && matchesRole;
    });
  }, [members, searchQuery, selectedRoleFilter]);

  // Statistics
  const totalCount = members.length;
  const adminCount = members.filter(
    (m) => m.sysAdmin || (m.roles || []).some((r) => r.sysAdmin || r.name === 'SystemAdministrators')
  ).length;
  const standardCount = totalCount - adminCount;

  // Handle Copy Email
  const handleCopyEmail = (email: string) => {
    navigator.clipboard.writeText(email);
    setCopiedEmail(email);
    setTimeout(() => setCopiedEmail(null), 2000);
  };

  // Open Invite Modal
  const openInviteModal = () => {
    setInviteEmail('');
    setInviteFirstName('');
    setInviteLastName('');
    // Default to first non-sysadmin role or general users
    const defaultRole = roles.find((r) => !r.sysAdmin && r.name === 'GeneralUsers') || roles[0];
    setInviteRoleIds(defaultRole ? [defaultRole.id] : []);
    setInviteError(null);
    setIsInviteOpen(true);
  };

  // Submit Invite
  const handleInviteSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!inviteEmail.trim()) {
      setInviteError('Email address is required.');
      return;
    }

    setIsSubmittingInvite(true);
    setInviteError(null);

    try {
      const res = await authFetch('/api/Administration/users', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          email: inviteEmail.trim(),
          firstName: inviteFirstName.trim() || undefined,
          lastName: inviteLastName.trim() || undefined,
          roleIds: inviteRoleIds,
        }),
      });

      if (res.ok) {
        setIsInviteOpen(false);
        showSuccess(`Member ${inviteEmail.trim()} successfully invited and saved to database.`);
        loadData(true);
      } else {
        const errorData = await res.json().catch(() => null);
        const errorText = errorData?.detail || errorData?.title || (await res.text()) || 'Failed to add member.';
        setInviteError(errorText);
      }
    } catch (err: any) {
      setInviteError(err?.message || 'Unexpected network error.');
    } finally {
      setIsSubmittingInvite(false);
    }
  };

  // Open Edit Modal
  const openEditModal = (member: TeamMember) => {
    setEditingMember(member);
    setEditFirstName(member.firstName || '');
    setEditLastName(member.lastName || '');
    setEditEmail(member.email || '');
    setEditRoleIds((member.roles || []).map((r) => r.id));
    setEditError(null);
  };

  // Submit Edit
  const handleEditSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!editingMember) return;

    setIsSubmittingEdit(true);
    setEditError(null);

    try {
      const res = await authFetch(`/api/Administration/users/${editingMember.id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          firstName: editFirstName.trim(),
          lastName: editLastName.trim(),
          email: editEmail.trim(),
          roleIds: editRoleIds,
        }),
      });

      if (res.ok) {
        setEditingMember(null);
        showSuccess(`Member ${editEmail} updated successfully in the database.`);
        loadData(true);
      } else {
        const errorData = await res.json().catch(() => null);
        const errorText = errorData?.detail || errorData?.title || (await res.text()) || 'Failed to update member.';
        setEditError(errorText);
      }
    } catch (err: any) {
      setEditError(err?.message || 'Unexpected network error.');
    } finally {
      setIsSubmittingEdit(false);
    }
  };

  // Open Delete Confirmation
  const openDeleteModal = (member: TeamMember) => {
    setDeletingMember(member);
    setDeleteError(null);
  };

  // Submit Delete
  const handleDeleteSubmit = async () => {
    if (!deletingMember) return;

    setIsSubmittingDelete(true);
    setDeleteError(null);

    try {
      const res = await authFetch(`/api/Administration/users/${deletingMember.id}`, {
        method: 'DELETE',
      });

      if (res.ok) {
        const email = deletingMember.email;
        setDeletingMember(null);
        showSuccess(`Member ${email} was permanently deleted from the database.`);
        loadData(true);
      } else {
        const errorData = await res.json().catch(() => null);
        const errorText = errorData?.detail || errorData?.title || (await res.text()) || 'Failed to delete member.';
        setDeleteError(errorText);
      }
    } catch (err: any) {
      setDeleteError(err?.message || 'Unexpected network error.');
    } finally {
      setIsSubmittingDelete(false);
    }
  };

  // Toggle role checkbox in invite/edit modal
  const toggleRoleId = (
    roleId: number,
    currentList: number[],
    setter: React.Dispatch<React.SetStateAction<number[]>>
  ) => {
    if (currentList.includes(roleId)) {
      setter(currentList.filter((id) => id !== roleId));
    } else {
      setter([...currentList, roleId]);
    }
  };

  // Selected role for Roles tab
  const activeRoleDefinition = roles.find((r) => r.id === selectedRoleId) || roles[0];

  return (
    <div className="space-y-6 w-full animate-in fade-in duration-200 text-[var(--text-main)]">
      {/* Workspace Banner */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 p-4 sm:p-5 rounded-2xl bg-gradient-to-r from-[var(--bg-surface)] via-[var(--bg-surface)] to-[var(--brand-subtle)] shadow-xl">
        <div className="space-y-1">
          <div className="flex items-center gap-2">
            <span className="w-2 h-2 rounded-full bg-[var(--brand-primary)] animate-pulse" />
            <span className="text-[10px] sm:text-xs font-semibold uppercase tracking-wider text-[var(--brand-primary)]">
              Organization Governance
            </span>
          </div>
          <h1 className="text-xl sm:text-2xl font-bold tracking-tight text-[var(--text-main)]">
            Team &amp; Members
          </h1>
          <p className="text-xs sm:text-sm text-[var(--text-muted)]">
            Manage authenticated users, assign role-based access control, and enforce tenant security for{' '}
            <span className="font-semibold text-[var(--text-main)]">{currentOrg.name}</span>.
          </p>
        </div>

        <div className="flex items-center gap-2.5">
          <button
            type="button"
            onClick={() => loadData(true)}
            disabled={isRefreshing}
            className="h-10 px-3.5 rounded-xl bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)] text-xs font-semibold text-[var(--text-main)] transition-all shadow-sm active:scale-[0.98] flex items-center gap-2 cursor-pointer disabled:opacity-50"
            title="Refresh live data from database"
          >
            <RefreshCw className={`w-3.5 h-3.5 ${isRefreshing ? 'animate-spin text-[var(--brand-primary)]' : ''}`} />
            <span className="hidden sm:inline">Sync DB</span>
          </button>

          <button
            type="button"
            onClick={openInviteModal}
            className="h-10 px-4 rounded-xl bg-[var(--brand-primary)] hover:brightness-110 text-white text-xs font-semibold transition-all shadow-md active:scale-[0.98] flex items-center gap-2 cursor-pointer"
          >
            <UserPlus className="w-4 h-4" />
            <span>+ Invite Member</span>
          </button>
        </div>
      </div>

      {/* Notifications */}
      {successBanner && (
        <div className="p-4 rounded-xl bg-emerald-500/10 border border-emerald-500/20 text-emerald-400 text-xs sm:text-sm flex items-center justify-between gap-3 shadow-md animate-in fade-in duration-200">
          <div className="flex items-center gap-2.5">
            <Check className="w-4 h-4 shrink-0" />
            <span>{successBanner}</span>
          </div>
          <button type="button" onClick={() => setSuccessBanner(null)} className="hover:opacity-75 cursor-pointer">
            <X className="w-4 h-4" />
          </button>
        </div>
      )}

      {errorBanner && (
        <div className="p-4 rounded-xl bg-red-500/10 border border-red-500/20 text-red-400 text-xs sm:text-sm flex items-center justify-between gap-3 shadow-md animate-in fade-in duration-200">
          <div className="flex items-center gap-2.5">
            <AlertTriangle className="w-4 h-4 shrink-0" />
            <span>{errorBanner}</span>
          </div>
          <button type="button" onClick={() => setErrorBanner(null)} className="hover:opacity-75 cursor-pointer">
            <X className="w-4 h-4" />
          </button>
        </div>
      )}

      {/* Metric Cards */}
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-3 sm:gap-4">
        <div className="p-4 sm:p-5 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-1">
          <div className="flex items-center justify-between text-[var(--text-muted)]">
            <span className="text-xs font-medium">Total Members</span>
            <Users className="w-4 h-4 text-[var(--brand-primary)]" />
          </div>
          <div className="text-2xl font-bold text-[var(--text-main)]">{totalCount}</div>
          <div className="text-[11px] text-[var(--text-muted)]">Live database accounts</div>
        </div>

        <div className="p-4 sm:p-5 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-1">
          <div className="flex items-center justify-between text-[var(--text-muted)]">
            <span className="text-xs font-medium">Administrators</span>
            <ShieldCheck className="w-4 h-4 text-purple-400" />
          </div>
          <div className="text-2xl font-bold text-[var(--text-main)]">{adminCount}</div>
          <div className="text-[11px] text-[var(--text-muted)]">Full system access</div>
        </div>

        <div className="p-4 sm:p-5 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-1">
          <div className="flex items-center justify-between text-[var(--text-muted)]">
            <span className="text-xs font-medium">Standard Roles</span>
            <Shield className="w-4 h-4 text-blue-400" />
          </div>
          <div className="text-2xl font-bold text-[var(--text-main)]">{standardCount}</div>
          <div className="text-[11px] text-[var(--text-muted)]">Role-restricted users</div>
        </div>

        <div className="p-4 sm:p-5 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-1">
          <div className="flex items-center justify-between text-[var(--text-muted)]">
            <span className="text-xs font-medium">Tenant Security</span>
            <Lock className="w-4 h-4 text-emerald-400" />
          </div>
          <div className="text-lg font-bold text-emerald-400">RBAC Enforced</div>
          <div className="text-[11px] text-[var(--text-muted)]">Token &amp; OTP security</div>
        </div>
      </div>

      {/* Tabs */}
      <div className="flex items-center gap-2 border-b border-[var(--border-subtle)] pb-2">
        <button
          type="button"
          onClick={() => setActiveTab('members')}
          className={`h-9 px-4 rounded-xl text-xs font-semibold transition-all cursor-pointer flex items-center gap-2 ${
            activeTab === 'members'
              ? 'bg-[var(--brand-primary)] text-white shadow-sm'
              : 'text-[var(--text-muted)] hover:text-[var(--text-main)] hover:bg-[var(--bg-subtle)]'
          }`}
        >
          <Users className="w-3.5 h-3.5" />
          <span>Authorized Members ({members.length})</span>
        </button>

        <button
          type="button"
          onClick={() => setActiveTab('roles')}
          className={`h-9 px-4 rounded-xl text-xs font-semibold transition-all cursor-pointer flex items-center gap-2 ${
            activeTab === 'roles'
              ? 'bg-[var(--brand-primary)] text-white shadow-sm'
              : 'text-[var(--text-muted)] hover:text-[var(--text-main)] hover:bg-[var(--bg-subtle)]'
          }`}
        >
          <Shield className="w-3.5 h-3.5" />
          <span>Roles &amp; Permissions Matrix ({roles.length})</span>
        </button>
      </div>

      {activeTab === 'members' ? (
        <div className="space-y-4">
          {/* Search & Filter Bar */}
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
            <div className="relative flex-1 max-w-md">
              <Search className="absolute left-3.5 top-1/2 -translate-y-1/2 w-4 h-4 text-[var(--text-muted)]" />
              <input
                type="text"
                placeholder="Search member by name, email, or role..."
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                className="w-full h-10 pl-10 pr-4 rounded-xl bg-[var(--bg-surface)] backdrop-blur-md shadow-sm text-xs text-[var(--text-main)] placeholder-[var(--text-muted)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)]"
              />
              {searchQuery && (
                <button
                  type="button"
                  onClick={() => setSearchQuery('')}
                  className="absolute right-3 top-1/2 -translate-y-1/2 text-[var(--text-muted)] hover:text-[var(--text-main)]"
                >
                  <X className="w-3.5 h-3.5" />
                </button>
              )}
            </div>

            <div className="flex items-center gap-2">
              <div className="relative">
                <select
                  value={selectedRoleFilter}
                  onChange={(e) => setSelectedRoleFilter(e.target.value)}
                  className="h-10 pl-3 pr-8 rounded-xl bg-[var(--bg-surface)] backdrop-blur-md shadow-sm text-xs text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)] cursor-pointer appearance-none"
                >
                  <option value="all">All Roles ({roles.length})</option>
                  {roles.map((r) => (
                    <option key={r.id} value={r.id.toString()}>
                      {r.displayName || r.name}
                    </option>
                  ))}
                </select>
                <ChevronDown className="absolute right-2.5 top-1/2 -translate-y-1/2 w-3.5 h-3.5 text-[var(--text-muted)] pointer-events-none" />
              </div>
            </div>
          </div>

          {/* Members Table */}
          <div className="rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md overflow-hidden">
            {isLoading ? (
              <div className="p-12 text-center space-y-3">
                <RefreshCw className="w-6 h-6 animate-spin text-[var(--brand-primary)] mx-auto" />
                <p className="text-xs text-[var(--text-muted)]">Loading members from database...</p>
              </div>
            ) : filteredMembers.length === 0 ? (
              <div className="p-12 text-center space-y-3">
                <Users className="w-8 h-8 text-[var(--text-muted)] mx-auto" />
                <h3 className="text-sm font-semibold text-[var(--text-main)]">No members found</h3>
                <p className="text-xs text-[var(--text-muted)] max-w-sm mx-auto">
                  {searchQuery
                    ? `No team members matched "${searchQuery}". Try a different search term or role filter.`
                    : 'No authorized members currently recorded in the database.'}
                </p>
                {searchQuery && (
                  <button
                    type="button"
                    onClick={() => {
                      setSearchQuery('');
                      setSelectedRoleFilter('all');
                    }}
                    className="h-8 px-3 rounded-lg bg-[var(--bg-subtle)] text-xs font-semibold text-[var(--brand-primary)] hover:bg-[var(--bg-surface-hover)] cursor-pointer"
                  >
                    Clear Filters
                  </button>
                )}
              </div>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-left text-xs">
                  <thead className="bg-[var(--bg-subtle)] text-[var(--text-muted)] uppercase tracking-wider text-[10px] font-semibold border-b border-[var(--border-subtle)]">
                    <tr>
                      <th className="py-3 px-4 sm:px-6">Member Profile</th>
                      <th className="py-3 px-4">Email</th>
                      <th className="py-3 px-4">Assigned Roles</th>
                      <th className="py-3 px-4">Status</th>
                      <th className="py-3 px-4 sm:px-6 text-right">Actions</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-[var(--border-subtle)]">
                    {filteredMembers.map((member) => {
                      const fullName =
                        `${member.firstName || ''} ${member.lastName || ''}`.trim() ||
                        member.userName ||
                        member.email;
                      const initial = fullName.charAt(0).toUpperCase();
                      const isCurrentUser = Boolean(
                        currentUserProfile?.email &&
                        member.email?.toLowerCase() === currentUserProfile.email.toLowerCase()
                      );
                      const isSysAdmin =
                        member.sysAdmin ||
                        (member.roles || []).some(
                          (r) => r.sysAdmin || r.name === 'SystemAdministrators'
                        );

                      return (
                        <tr
                          key={member.id}
                          className="hover:bg-[var(--bg-surface-hover)]/50 transition-colors"
                        >
                          <td className="py-4 px-4 sm:px-6">
                            <div className="flex items-center gap-3">
                              <div
                                className={`w-9 h-9 rounded-xl flex items-center justify-center font-bold text-xs ring-1 shrink-0 ${
                                  isSysAdmin
                                    ? 'bg-purple-500/15 text-purple-400 ring-purple-500/30'
                                    : 'bg-[var(--brand-subtle)] text-[var(--brand-primary)] ring-[var(--brand-ring)]'
                                }`}
                              >
                                {initial}
                              </div>
                              <div className="min-w-0">
                                <div className="font-semibold text-[var(--text-main)] flex items-center gap-2 truncate">
                                  <span>{fullName}</span>
                                  {isCurrentUser && (
                                    <span className="px-1.5 py-0.5 rounded-full bg-[var(--brand-subtle)] text-[var(--brand-primary)] text-[10px] font-bold">
                                      You
                                    </span>
                                  )}
                                </div>
                                <div className="text-[11px] text-[var(--text-muted)] truncate">
                                  User ID: #{member.id}
                                </div>
                              </div>
                            </div>
                          </td>

                          <td className="py-4 px-4">
                            <div className="flex items-center gap-1.5 text-[var(--text-muted)] group">
                              <Mail className="w-3.5 h-3.5 shrink-0" />
                              <span className="font-mono text-[11px] text-[var(--text-main)] select-all truncate">
                                {member.email}
                              </span>
                              <button
                                type="button"
                                onClick={() => handleCopyEmail(member.email)}
                                className="opacity-0 group-hover:opacity-100 hover:text-[var(--text-main)] transition-opacity cursor-pointer p-0.5"
                                title="Copy Email"
                              >
                                {copiedEmail === member.email ? (
                                  <Check className="w-3 h-3 text-emerald-400" />
                                ) : (
                                  <Copy className="w-3 h-3" />
                                )}
                              </button>
                            </div>
                          </td>

                          <td className="py-4 px-4">
                            <div className="flex flex-wrap gap-1.5 max-w-xs">
                              {member.roles && member.roles.length > 0 ? (
                                member.roles.map((r) => (
                                  <span
                                    key={r.id}
                                    className={`px-2 py-0.5 rounded-lg text-[10px] font-semibold ${
                                      r.sysAdmin || r.name === 'SystemAdministrators'
                                        ? 'bg-purple-500/15 text-purple-400 ring-1 ring-purple-500/30'
                                        : 'bg-[var(--bg-subtle)] text-[var(--text-main)] ring-1 ring-[var(--border-subtle)]'
                                    }`}
                                  >
                                    {r.displayName || r.name}
                                  </span>
                                ))
                              ) : (
                                <span className="text-[11px] text-[var(--text-muted)] italic">
                                  No roles assigned
                                </span>
                              )}
                            </div>
                          </td>

                          <td className="py-4 px-4">
                            <span className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-full bg-emerald-500/10 text-emerald-400 text-[11px] font-medium">
                              <span className="w-1.5 h-1.5 rounded-full bg-emerald-400 animate-pulse" />
                              Active
                            </span>
                          </td>

                          <td className="py-4 px-4 sm:px-6 text-right">
                            <div className="flex items-center justify-end gap-1">
                              <button
                                type="button"
                                onClick={() => openEditModal(member)}
                                className="p-1.5 rounded-lg hover:bg-[var(--bg-subtle)] text-[var(--text-muted)] hover:text-[var(--text-main)] transition-colors cursor-pointer"
                                title="Edit Member Profile & Roles"
                              >
                                <Edit2 className="w-4 h-4" />
                              </button>

                              <button
                                type="button"
                                onClick={() => openDeleteModal(member)}
                                disabled={isCurrentUser}
                                className={`p-1.5 rounded-lg hover:bg-red-500/15 text-[var(--text-muted)] hover:text-red-400 transition-colors cursor-pointer ${
                                  isCurrentUser ? 'opacity-30 cursor-not-allowed hover:bg-transparent hover:text-[var(--text-muted)]' : ''
                                }`}
                                title={isCurrentUser ? 'Cannot delete your own account' : 'Remove Member'}
                              >
                                <Trash2 className="w-4 h-4" />
                              </button>
                            </div>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </div>
      ) : (
        /* Roles & Permissions Matrix Tab */
        <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
          {/* Roles Selector */}
          <div className="lg:col-span-1 space-y-3">
            <h3 className="text-sm font-bold text-[var(--text-main)]">Configured Roles</h3>
            <p className="text-xs text-[var(--text-muted)]">
              Select a role to inspect all granular permissions persisted in the database.
            </p>

            <div className="space-y-2 pt-2">
              {roles.map((role) => {
                const isSelected = selectedRoleId === role.id;
                const isSysAdmin = role.sysAdmin || role.name === 'SystemAdministrators';

                return (
                  <button
                    key={role.id}
                    type="button"
                    onClick={() => setSelectedRoleId(role.id)}
                    className={`w-full p-4 rounded-xl text-left transition-all cursor-pointer flex flex-col justify-between space-y-2 ${
                      isSelected
                        ? 'bg-[var(--brand-primary)] text-white shadow-md'
                        : 'bg-[var(--bg-surface)] hover:bg-[var(--bg-surface-hover)] text-[var(--text-main)] shadow-sm'
                    }`}
                  >
                    <div className="flex items-center justify-between w-full">
                      <div className="font-semibold text-xs sm:text-sm flex items-center gap-2">
                        {isSysAdmin ? (
                          <ShieldCheck className="w-4 h-4 text-purple-300 shrink-0" />
                        ) : (
                          <Shield className="w-4 h-4 text-[var(--brand-primary)] shrink-0" />
                        )}
                        <span className="truncate">{role.displayName || role.name}</span>
                      </div>
                      {isSysAdmin && (
                        <span
                          className={`px-1.5 py-0.5 rounded text-[10px] font-bold uppercase tracking-wider ${
                            isSelected ? 'bg-white/20 text-white' : 'bg-purple-500/20 text-purple-400'
                          }`}
                        >
                          System
                        </span>
                      )}
                    </div>
                    <div
                      className={`text-[11px] flex items-center justify-between ${
                        isSelected ? 'text-white/80' : 'text-[var(--text-muted)]'
                      }`}
                    >
                      <span>Role Key: {role.name}</span>
                      <span>
                        {isSysAdmin
                          ? 'Full Permissions'
                          : `${role.permissions?.length || 0} Permissions`}
                      </span>
                    </div>
                  </button>
                );
              })}
            </div>
          </div>

          {/* Granular Permission Details */}
          <div className="lg:col-span-2 space-y-4">
            <div className="p-6 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-4">
              <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2 pb-3 border-b border-[var(--border-subtle)]">
                <div>
                  <h3 className="text-base font-bold text-[var(--text-main)] flex items-center gap-2">
                    {activeRoleDefinition?.displayName || activeRoleDefinition?.name}
                    {activeRoleDefinition?.sysAdmin && (
                      <span className="px-2 py-0.5 rounded-full bg-purple-500/20 text-purple-400 text-xs font-semibold">
                        System Administrator
                      </span>
                    )}
                  </h3>
                  <p className="text-xs text-[var(--text-muted)] mt-0.5">
                    Role ID #{activeRoleDefinition?.id} persisted in the SQLite/database repository.
                  </p>
                </div>
              </div>

              {activeRoleDefinition?.sysAdmin || activeRoleDefinition?.name === 'SystemAdministrators' ? (
                <div className="p-4 rounded-xl bg-purple-500/10 border border-purple-500/20 text-purple-300 text-xs space-y-1">
                  <div className="font-bold flex items-center gap-2">
                    <ShieldCheck className="w-4 h-4 text-purple-400" />
                    Unrestricted Tenant Privileges
                  </div>
                  <p className="text-[11px] text-purple-300/80">
                    Users assigned to this role bypass permission checks and are granted read, create, edit,
                    and post privileges across all modules, including Administration and Subscription management.
                  </p>
                </div>
              ) : null}

              {/* Granted Permissions List */}
              <div className="space-y-3">
                <h4 className="text-xs font-bold uppercase tracking-wider text-[var(--text-muted)]">
                  Granted Database Permissions ({activeRoleDefinition?.permissions?.length || 0})
                </h4>

                {(!activeRoleDefinition?.permissions || activeRoleDefinition.permissions.length === 0) &&
                !activeRoleDefinition?.sysAdmin ? (
                  <div className="p-6 rounded-xl bg-[var(--bg-subtle)] text-center text-xs text-[var(--text-muted)]">
                    No specific individual permissions assigned to this role yet.
                  </div>
                ) : (
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-2 max-h-96 overflow-y-auto pr-1">
                    {(activeRoleDefinition?.permissions || []).map((p) => (
                      <div
                        key={p.id || p.name}
                        className="p-2.5 rounded-xl bg-[var(--bg-subtle)] flex items-center gap-2.5 text-xs text-[var(--text-main)]"
                      >
                        <Check className="w-3.5 h-3.5 text-emerald-400 shrink-0" />
                        <div className="min-w-0">
                          <div className="font-semibold truncate">{p.displayName || p.name}</div>
                          <div className="text-[10px] text-[var(--text-muted)] font-mono truncate">{p.name}</div>
                        </div>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Invite Member Modal */}
      {isInviteOpen && (
        <div className="fixed inset-0 z-50 bg-black/60 backdrop-blur-sm flex items-center justify-center p-4 animate-in fade-in duration-200">
          <div className="w-full max-w-lg rounded-2xl bg-[var(--bg-surface)] backdrop-blur-2xl shadow-2xl overflow-hidden border border-[var(--border-subtle)]">
            <div className="p-6 space-y-4">
              <div className="flex items-center justify-between pb-2 border-b border-[var(--border-subtle)]">
                <div className="flex items-center gap-2.5">
                  <UserPlus className="w-5 h-5 text-[var(--brand-primary)]" />
                  <h3 className="text-base font-bold text-[var(--text-main)]">Invite New Team Member</h3>
                </div>
                <button
                  type="button"
                  onClick={() => setIsInviteOpen(false)}
                  className="p-1 rounded-lg text-[var(--text-muted)] hover:text-[var(--text-main)] hover:bg-[var(--bg-subtle)] cursor-pointer"
                >
                  <X className="w-4 h-4" />
                </button>
              </div>

              {inviteError && (
                <div className="p-3 rounded-xl bg-red-500/10 border border-red-500/20 text-red-400 text-xs flex items-center gap-2">
                  <AlertTriangle className="w-4 h-4 shrink-0" />
                  <span>{inviteError}</span>
                </div>
              )}

              <form onSubmit={handleInviteSubmit} className="space-y-4">
                <div className="grid grid-cols-2 gap-3">
                  <div className="space-y-1.5">
                    <label className="text-xs font-semibold text-[var(--text-main)]">First Name</label>
                    <input
                      type="text"
                      placeholder="e.g. Maria"
                      value={inviteFirstName}
                      onChange={(e) => setInviteFirstName(e.target.value)}
                      className="w-full h-10 px-3 rounded-xl bg-[var(--bg-subtle)] text-xs text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)]"
                    />
                  </div>

                  <div className="space-y-1.5">
                    <label className="text-xs font-semibold text-[var(--text-main)]">Last Name</label>
                    <input
                      type="text"
                      placeholder="e.g. Santos"
                      value={inviteLastName}
                      onChange={(e) => setInviteLastName(e.target.value)}
                      className="w-full h-10 px-3 rounded-xl bg-[var(--bg-subtle)] text-xs text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)]"
                    />
                  </div>
                </div>

                <div className="space-y-1.5">
                  <label className="text-xs font-semibold text-[var(--text-main)]">
                    Email Address <span className="text-red-400">*</span>
                  </label>
                  <input
                    type="email"
                    required
                    placeholder="maria.santos@accountgo.ph"
                    value={inviteEmail}
                    onChange={(e) => setInviteEmail(e.target.value)}
                    className="w-full h-10 px-3 rounded-xl bg-[var(--bg-subtle)] text-xs text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)] font-mono"
                  />
                  <p className="text-[11px] text-[var(--text-muted)]">
                    The member will use this email address to authenticate with OTP or Google sign-in.
                  </p>
                </div>

                <div className="space-y-2 pt-1">
                  <label className="text-xs font-semibold text-[var(--text-main)]">Assign Roles</label>
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-2 max-h-48 overflow-y-auto p-1">
                    {roles.map((r) => {
                      const isChecked = inviteRoleIds.includes(r.id);
                      return (
                        <label
                          key={r.id}
                          className={`p-2.5 rounded-xl border text-xs flex items-center justify-between cursor-pointer transition-colors ${
                            isChecked
                              ? 'bg-[var(--brand-subtle)] border-[var(--brand-ring)] text-[var(--text-main)]'
                              : 'bg-[var(--bg-subtle)] border-transparent text-[var(--text-muted)] hover:text-[var(--text-main)]'
                          }`}
                        >
                          <div className="flex items-center gap-2 min-w-0">
                            <input
                              type="checkbox"
                              checked={isChecked}
                              onChange={() => toggleRoleId(r.id, inviteRoleIds, setInviteRoleIds)}
                              className="w-3.5 h-3.5 rounded text-[var(--brand-primary)] focus:ring-0 cursor-pointer"
                            />
                            <span className="font-semibold truncate">{r.displayName || r.name}</span>
                          </div>
                          {r.sysAdmin && (
                            <span className="text-[10px] text-purple-400 font-bold">Admin</span>
                          )}
                        </label>
                      );
                    })}
                  </div>
                </div>

                <div className="flex items-center justify-end gap-2.5 pt-4 border-t border-[var(--border-subtle)]">
                  <button
                    type="button"
                    onClick={() => setIsInviteOpen(false)}
                    disabled={isSubmittingInvite}
                    className="h-10 px-4 rounded-xl bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)] text-xs font-semibold text-[var(--text-main)] cursor-pointer"
                  >
                    Cancel
                  </button>
                  <button
                    type="submit"
                    disabled={isSubmittingInvite}
                    className="h-10 px-5 rounded-xl bg-[var(--brand-primary)] hover:brightness-110 text-white text-xs font-semibold transition-all shadow-md active:scale-[0.98] cursor-pointer flex items-center gap-2 disabled:opacity-50"
                  >
                    {isSubmittingInvite ? (
                      <>
                        <RefreshCw className="w-3.5 h-3.5 animate-spin" />
                        <span>Inviting...</span>
                      </>
                    ) : (
                      <span>Save &amp; Invite Member</span>
                    )}
                  </button>
                </div>
              </form>
            </div>
          </div>
        </div>
      )}

      {/* Edit Member Modal */}
      {editingMember && (
        <div className="fixed inset-0 z-50 bg-black/60 backdrop-blur-sm flex items-center justify-center p-4 animate-in fade-in duration-200">
          <div className="w-full max-w-lg rounded-2xl bg-[var(--bg-surface)] backdrop-blur-2xl shadow-2xl overflow-hidden border border-[var(--border-subtle)]">
            <div className="p-6 space-y-4">
              <div className="flex items-center justify-between pb-2 border-b border-[var(--border-subtle)]">
                <div className="flex items-center gap-2.5">
                  <Edit2 className="w-5 h-5 text-[var(--brand-primary)]" />
                  <h3 className="text-base font-bold text-[var(--text-main)]">
                    Edit Member (ID #{editingMember.id})
                  </h3>
                </div>
                <button
                  type="button"
                  onClick={() => setEditingMember(null)}
                  className="p-1 rounded-lg text-[var(--text-muted)] hover:text-[var(--text-main)] hover:bg-[var(--bg-subtle)] cursor-pointer"
                >
                  <X className="w-4 h-4" />
                </button>
              </div>

              {editError && (
                <div className="p-3 rounded-xl bg-red-500/10 border border-red-500/20 text-red-400 text-xs flex items-center gap-2">
                  <AlertTriangle className="w-4 h-4 shrink-0" />
                  <span>{editError}</span>
                </div>
              )}

              <form onSubmit={handleEditSubmit} className="space-y-4">
                <div className="grid grid-cols-2 gap-3">
                  <div className="space-y-1.5">
                    <label className="text-xs font-semibold text-[var(--text-main)]">First Name</label>
                    <input
                      type="text"
                      value={editFirstName}
                      onChange={(e) => setEditFirstName(e.target.value)}
                      className="w-full h-10 px-3 rounded-xl bg-[var(--bg-subtle)] text-xs text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)]"
                    />
                  </div>

                  <div className="space-y-1.5">
                    <label className="text-xs font-semibold text-[var(--text-main)]">Last Name</label>
                    <input
                      type="text"
                      value={editLastName}
                      onChange={(e) => setEditLastName(e.target.value)}
                      className="w-full h-10 px-3 rounded-xl bg-[var(--bg-subtle)] text-xs text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)]"
                    />
                  </div>
                </div>

                <div className="space-y-1.5">
                  <label className="text-xs font-semibold text-[var(--text-main)]">Email Address</label>
                  <input
                    type="email"
                    required
                    value={editEmail}
                    onChange={(e) => setEditEmail(e.target.value)}
                    className="w-full h-10 px-3 rounded-xl bg-[var(--bg-subtle)] text-xs text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)] font-mono"
                  />
                </div>

                <div className="space-y-2 pt-1">
                  <label className="text-xs font-semibold text-[var(--text-main)]">Assigned Roles</label>
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-2 max-h-48 overflow-y-auto p-1">
                    {roles.map((r) => {
                      const isChecked = editRoleIds.includes(r.id);
                      return (
                        <label
                          key={r.id}
                          className={`p-2.5 rounded-xl border text-xs flex items-center justify-between cursor-pointer transition-colors ${
                            isChecked
                              ? 'bg-[var(--brand-subtle)] border-[var(--brand-ring)] text-[var(--text-main)]'
                              : 'bg-[var(--bg-subtle)] border-transparent text-[var(--text-muted)] hover:text-[var(--text-main)]'
                          }`}
                        >
                          <div className="flex items-center gap-2 min-w-0">
                            <input
                              type="checkbox"
                              checked={isChecked}
                              onChange={() => toggleRoleId(r.id, editRoleIds, setEditRoleIds)}
                              className="w-3.5 h-3.5 rounded text-[var(--brand-primary)] focus:ring-0 cursor-pointer"
                            />
                            <span className="font-semibold truncate">{r.displayName || r.name}</span>
                          </div>
                          {r.sysAdmin && (
                            <span className="text-[10px] text-purple-400 font-bold">Admin</span>
                          )}
                        </label>
                      );
                    })}
                  </div>
                </div>

                <div className="flex items-center justify-end gap-2.5 pt-4 border-t border-[var(--border-subtle)]">
                  <button
                    type="button"
                    onClick={() => setEditingMember(null)}
                    disabled={isSubmittingEdit}
                    className="h-10 px-4 rounded-xl bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)] text-xs font-semibold text-[var(--text-main)] cursor-pointer"
                  >
                    Cancel
                  </button>
                  <button
                    type="submit"
                    disabled={isSubmittingEdit}
                    className="h-10 px-5 rounded-xl bg-[var(--brand-primary)] hover:brightness-110 text-white text-xs font-semibold transition-all shadow-md active:scale-[0.98] cursor-pointer flex items-center gap-2 disabled:opacity-50"
                  >
                    {isSubmittingEdit ? (
                      <>
                        <RefreshCw className="w-3.5 h-3.5 animate-spin" />
                        <span>Updating...</span>
                      </>
                    ) : (
                      <span>Save Changes</span>
                    )}
                  </button>
                </div>
              </form>
            </div>
          </div>
        </div>
      )}

      {/* Delete Member Confirmation Modal */}
      {deletingMember && (
        <div className="fixed inset-0 z-50 bg-black/60 backdrop-blur-sm flex items-center justify-center p-4 animate-in fade-in duration-200">
          <div className="w-full max-w-md rounded-2xl bg-[var(--bg-surface)] backdrop-blur-2xl shadow-2xl overflow-hidden border border-red-500/20">
            <div className="p-6 space-y-4">
              <div className="flex items-center gap-3 text-red-400">
                <div className="w-10 h-10 rounded-xl bg-red-500/15 flex items-center justify-center shrink-0">
                  <AlertTriangle className="w-5 h-5 text-red-400" />
                </div>
                <div>
                  <h3 className="text-base font-bold text-[var(--text-main)]">Remove Member</h3>
                  <p className="text-xs text-[var(--text-muted)]">This action will revoke access immediately.</p>
                </div>
              </div>

              {deleteError && (
                <div className="p-3 rounded-xl bg-red-500/10 border border-red-500/20 text-red-400 text-xs flex items-center gap-2">
                  <AlertTriangle className="w-4 h-4 shrink-0" />
                  <span>{deleteError}</span>
                </div>
              )}

              <p className="text-xs text-[var(--text-muted)] leading-relaxed">
                Are you sure you want to remove{' '}
                <strong className="text-[var(--text-main)]">{deletingMember.email}</strong> from{' '}
                <strong className="text-[var(--text-main)]">{currentOrg.name}</strong>? All active sessions and
                permissions for this user will be revoked immediately.
              </p>

              <div className="flex items-center justify-end gap-2.5 pt-3 border-t border-[var(--border-subtle)]">
                <button
                  type="button"
                  onClick={() => setDeletingMember(null)}
                  disabled={isSubmittingDelete}
                  className="h-10 px-4 rounded-xl bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)] text-xs font-semibold text-[var(--text-main)] cursor-pointer"
                >
                  Cancel
                </button>
                <button
                  type="button"
                  onClick={handleDeleteSubmit}
                  disabled={isSubmittingDelete}
                  className="h-10 px-5 rounded-xl bg-red-600 hover:bg-red-500 text-white text-xs font-semibold transition-all shadow-md active:scale-[0.98] cursor-pointer flex items-center gap-2 disabled:opacity-50"
                >
                  {isSubmittingDelete ? (
                    <>
                      <RefreshCw className="w-3.5 h-3.5 animate-spin" />
                      <span>Deleting...</span>
                    </>
                  ) : (
                    <span>Permanently Remove</span>
                  )}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default Members;
