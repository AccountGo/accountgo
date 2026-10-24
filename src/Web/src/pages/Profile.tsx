import React, { useState, useEffect, useCallback, useRef } from 'react';
import { useNavigate, useOutletContext } from 'react-router-dom';
import {
  User,
  Shield,
  ShieldCheck,
  Lock,
  Key,
  Smartphone,
  Mail,
  Save,
  Check,
  AlertTriangle,
  RefreshCw,
  Globe,
  Trash2,
  Building2,
  Laptop,
} from 'lucide-react';
import { HeaderButton } from '../components/PageHeader';
import { TenantContextType } from '../types';
import { useAuth } from '../auth/AuthContext';
import { UserSession } from '../types/auth';
import { clearPersistedOrgSlug } from '../lib/storage';

type DeletionBlockers = {
  workspaces: { name: string; slug: string }[];
  platformAdministrator: boolean;
};

export const Profile: React.FC = () => {
  const tenantContext = useOutletContext<TenantContextType>();
  const { currentOrg, availableOrgs, switchOrg, userPermissions } = tenantContext;
  const navigate = useNavigate();
  const { user, authFetch, refreshUser, clearSession } = useAuth();

  // Profile fields state
  const [firstName, setFirstName] = useState(user?.firstName || '');
  const [lastName, setLastName] = useState(user?.lastName || '');
  const [phoneNumber, setPhoneNumber] = useState(user?.phoneNumber || '');
  const [isSavingProfile, setIsSavingProfile] = useState(false);
  const [profileSuccess, setProfileSuccess] = useState<string | null>(null);
  const [profileError, setProfileError] = useState<string | null>(null);

  // Password fields state
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [isSavingPassword, setIsSavingPassword] = useState(false);
  const [passwordSuccess, setPasswordSuccess] = useState<string | null>(null);
  const [passwordError, setPasswordError] = useState<string | null>(null);

  // Sessions state
  const [sessions, setSessions] = useState<UserSession[]>([]);
  const [isLoadingSessions, setIsLoadingSessions] = useState(false);
  const [sessionError, setSessionError] = useState<string | null>(null);
  const [isRevokingAll, setIsRevokingAll] = useState(false);

  const [deleteDialogOpen, setDeleteDialogOpen] = useState(false);
  const [deleteEmail, setDeleteEmail] = useState('');
  const [deleteError, setDeleteError] = useState('');
  const [deletingAccount, setDeletingAccount] = useState(false);
  const [loadingBlockers, setLoadingBlockers] = useState(false);
  const [blockers, setBlockers] = useState<DeletionBlockers>({ workspaces: [], platformAdministrator: false });
  const deleteDialogRef = useRef<HTMLFormElement>(null);
  const deleteConfirmRef = useRef<HTMLInputElement>(null);

  // Sync state if user changes
  useEffect(() => {
    if (user) {
      setFirstName(user.firstName || '');
      setLastName(user.lastName || '');
      setPhoneNumber(user.phoneNumber || '');
    }
  }, [user]);

  // Load active sessions from database
  const loadSessions = useCallback(async () => {
    setIsLoadingSessions(true);
    setSessionError(null);
    try {
      const res = await authFetch('/api/auth/sessions');
      if (res.ok) {
        const data = await res.json();
        if (Array.isArray(data)) {
          setSessions(data);
        }
      }
    } catch (err: any) {
      setSessionError(err?.message || 'Failed to load active sessions.');
    } finally {
      setIsLoadingSessions(false);
    }
  }, [authFetch]);

  useEffect(() => {
    loadSessions();
  }, [loadSessions]);

  // Handle Profile Save
  const handleProfileSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsSavingProfile(true);
    setProfileSuccess(null);
    setProfileError(null);

    try {
      const res = await authFetch('/api/auth/profile', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          firstName: firstName.trim(),
          lastName: lastName.trim(),
          phoneNumber: phoneNumber.trim() || undefined,
        }),
      });

      if (res.ok) {
        setProfileSuccess('Personal profile successfully updated in database.');
        if (refreshUser) {
          await refreshUser();
        }
        setTimeout(() => setProfileSuccess(null), 4000);
      } else {
        const errorData = await res.json().catch(() => null);
        const errorText = errorData?.detail || errorData?.title || (await res.text()) || 'Failed to update profile.';
        setProfileError(errorText);
      }
    } catch (err: any) {
      setProfileError(err?.message || 'Network error updating profile.');
    } finally {
      setIsSavingProfile(false);
    }
  };

  // Handle Password Update
  const handlePasswordSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setPasswordSuccess(null);
    setPasswordError(null);

    if (newPassword.length < 6) {
      setPasswordError('New password must be at least 6 characters.');
      return;
    }

    if (newPassword !== confirmPassword) {
      setPasswordError('New password and confirmation do not match.');
      return;
    }

    setIsSavingPassword(true);

    try {
      const res = await authFetch('/api/auth/change-password', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          currentPassword: currentPassword ? currentPassword.trim() : undefined,
          newPassword: newPassword.trim(),
        }),
      });

      if (res.ok) {
        setPasswordSuccess('Password successfully updated and encrypted in database.');
        setCurrentPassword('');
        setNewPassword('');
        setConfirmPassword('');
        if (refreshUser) {
          await refreshUser();
        }
        setTimeout(() => setPasswordSuccess(null), 4000);
      } else {
        const errorData = await res.json().catch(() => null);
        const errorText = errorData?.detail || errorData?.title || (await res.text()) || 'Failed to update password.';
        setPasswordError(errorText);
      }
    } catch (err: any) {
      setPasswordError(err?.message || 'Network error updating password.');
    } finally {
      setIsSavingPassword(false);
    }
  };

  // Revoke single session
  const handleRevokeSession = async (sessionId: number) => {
    try {
      const res = await authFetch(`/api/auth/sessions/${sessionId}`, {
        method: 'DELETE',
      });
      if (res.ok) {
        setSessions((prev) => prev.filter((s) => s.id !== sessionId));
      }
    } catch {}
  };

  // Revoke all other sessions
  const handleRevokeOtherSessions = async () => {
    setIsRevokingAll(true);
    try {
      const res = await authFetch('/api/auth/sessions/revoke-others', {
        method: 'POST',
      });
      if (res.ok) {
        loadSessions();
      }
    } catch {} finally {
      setIsRevokingAll(false);
    }
  };

  const openDeleteDialog = async () => {
    setDeleteEmail('');
    setDeleteError('');
    setBlockers({ workspaces: [], platformAdministrator: false });
    setDeleteDialogOpen(true);
    setLoadingBlockers(true);
    try {
      const res = await authFetch('/api/auth/account/deletion-blockers');
      if (!res.ok) {
        setDeleteError('Could not check whether this account can be deleted.');
        return;
      }
      setBlockers(await res.json());
    } catch {
      setDeleteError('Could not check whether this account can be deleted.');
    } finally {
      setLoadingBlockers(false);
    }
  };

  const accountDeletionBlocked = blockers.platformAdministrator || blockers.workspaces.length > 0;

  useEffect(() => {
    if (deleteDialogOpen && !loadingBlockers && !accountDeletionBlocked) {
      deleteConfirmRef.current?.focus();
    }
  }, [deleteDialogOpen, loadingBlockers, accountDeletionBlocked]);

  const handleDeleteAccount = async (event: React.FormEvent) => {
    event.preventDefault();
    if (deletingAccount || accountDeletionBlocked || deleteEmail.trim().toLowerCase() !== (user?.email || '').toLowerCase()) return;
    setDeletingAccount(true);
    setDeleteError('');
    try {
      const res = await authFetch('/api/auth/account', {
        method: 'DELETE',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ confirmEmail: deleteEmail.trim() }),
      });
      if (!res.ok) {
        const problem = await res.json().catch(() => null) as { title?: string; detail?: string } | null;
        setDeleteError(problem?.title || problem?.detail || 'Account could not be deleted.');
        return;
      }
      clearPersistedOrgSlug();
      clearSession();
      navigate('/login', { replace: true, state: { notice: 'Account deleted.' } });
    } catch {
      setDeleteError('Account could not be deleted. Please try again.');
    } finally {
      setDeletingAccount(false);
    }
  };

  const displayName =
    `${user?.firstName || ''} ${user?.lastName || ''}`.trim() || user?.email || 'User';
  const initial = displayName.charAt(0).toUpperCase();
  // Platform staff role (global); workspace access comes from the tenant context below.
  const isSysAdmin = (user?.roles || []).includes('SystemAdministrators');
  const hasFullWorkspaceAccess = currentOrg.role === 'Owner' || currentOrg.role === 'Admin';

  return (
    <div className="space-y-6 w-full animate-in fade-in duration-200 text-[var(--text-main)]">
      {/* Workspace Context Top Bar */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 p-4 sm:p-5 rounded-2xl bg-gradient-to-r from-[var(--bg-surface)] via-[var(--bg-surface)] to-[var(--brand-subtle)] shadow-xl">
        <div className="flex items-center gap-4">
          <div
            className={`w-12 h-12 rounded-2xl flex items-center justify-center font-bold text-lg ring-2 shrink-0 ${
              isSysAdmin
                ? 'bg-purple-500/20 text-purple-400 ring-purple-500/30'
                : 'bg-[var(--brand-subtle)] text-[var(--brand-primary)] ring-[var(--brand-ring)]'
            }`}
          >
            {initial}
          </div>
          <div>
            <div className="flex items-center gap-2">
              <span className="w-2 h-2 rounded-full bg-[var(--brand-primary)] animate-pulse" />
              <span className="text-[10px] sm:text-xs font-semibold uppercase tracking-wider text-[var(--brand-primary)]">
                Account Security &amp; Identity
              </span>
            </div>
            <h1 className="text-xl sm:text-2xl font-bold tracking-tight text-[var(--text-main)] flex items-center gap-2">
              <span>{displayName}</span>
              {isSysAdmin && (
                <span title="System Administrator">
                  <ShieldCheck className="w-5 h-5 text-purple-400" />
                </span>
              )}
            </h1>
            <p className="text-xs text-[var(--text-muted)]">{user?.email}</p>
          </div>
        </div>

        <div className="flex items-center gap-2">
          <HeaderButton
            label="Sync"
            icon={RefreshCw}
            onClick={() => {
              if (refreshUser) refreshUser();
              loadSessions();
            }}
          />
        </div>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        {/* Left Column: Personal Info & Password Settings */}
        <div className="lg:col-span-2 space-y-6">
          {/* Card 1: Personal Details */}
          <div className="p-6 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-4">
            <div className="flex items-center gap-2.5 pb-2 border-b border-[var(--border-subtle)]">
              <User className="w-5 h-5 text-[var(--brand-primary)]" />
              <div>
                <h2 className="text-base font-bold text-[var(--text-main)]">Personal Information</h2>
                <p className="text-xs text-[var(--text-muted)]">
                  Update your personal profile details saved in the database.
                </p>
              </div>
            </div>

            {profileSuccess && (
              <div className="p-3 rounded-xl bg-emerald-500/10 border border-emerald-500/20 text-emerald-400 text-xs flex items-center gap-2">
                <Check className="w-4 h-4 shrink-0" />
                <span>{profileSuccess}</span>
              </div>
            )}

            {profileError && (
              <div className="p-3 rounded-xl bg-red-500/10 border border-red-500/20 text-red-400 text-xs flex items-center gap-2">
                <AlertTriangle className="w-4 h-4 shrink-0" />
                <span>{profileError}</span>
              </div>
            )}

            <form onSubmit={handleProfileSubmit} className="space-y-4">
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                <div className="space-y-1.5">
                  <label className="text-xs font-semibold text-[var(--text-main)]">First Name</label>
                  <input
                    type="text"
                    value={firstName}
                    onChange={(e) => setFirstName(e.target.value)}
                    placeholder="Enter first name"
                    className="w-full h-10 px-3.5 rounded-xl bg-[var(--bg-subtle)] text-xs text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)]"
                  />
                </div>

                <div className="space-y-1.5">
                  <label className="text-xs font-semibold text-[var(--text-main)]">Last Name</label>
                  <input
                    type="text"
                    value={lastName}
                    onChange={(e) => setLastName(e.target.value)}
                    placeholder="Enter last name"
                    className="w-full h-10 px-3.5 rounded-xl bg-[var(--bg-subtle)] text-xs text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)]"
                  />
                </div>
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                <div className="space-y-1.5">
                  <label className="text-xs font-semibold text-[var(--text-main)] flex items-center gap-1.5">
                    <Mail className="w-3.5 h-3.5 text-[var(--text-muted)]" />
                    <span>Email Address</span>
                  </label>
                  <input
                    type="email"
                    disabled
                    value={user?.email || ''}
                    className="w-full h-10 px-3.5 rounded-xl bg-[var(--bg-subtle)] opacity-70 cursor-not-allowed font-mono text-xs text-[var(--text-main)]"
                  />
                  <p className="text-[10px] text-[var(--text-muted)]">
                    Primary authentication identity. Managed by tenant administrator.
                  </p>
                </div>

                <div className="space-y-1.5">
                  <label className="text-xs font-semibold text-[var(--text-main)] flex items-center gap-1.5">
                    <Smartphone className="w-3.5 h-3.5 text-[var(--text-muted)]" />
                    <span>Contact Phone</span>
                  </label>
                  <input
                    type="tel"
                    value={phoneNumber}
                    onChange={(e) => setPhoneNumber(e.target.value)}
                    placeholder="+63 912 345 6789"
                    className="w-full h-10 px-3.5 rounded-xl bg-[var(--bg-subtle)] text-xs text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)] font-mono"
                  />
                  <p className="text-[10px] text-[var(--text-muted)]">
                    Optional phone number for recovery and SMS notifications.
                  </p>
                </div>
              </div>

              <div className="flex justify-end pt-2">
                <button
                  type="submit"
                  disabled={isSavingProfile}
                  className="h-10 px-5 rounded-xl bg-[var(--brand-primary)] hover:brightness-110 text-white text-xs font-semibold transition-all shadow-md active:scale-[0.98] flex items-center gap-2 cursor-pointer disabled:opacity-50"
                >
                  {isSavingProfile ? (
                    <>
                      <RefreshCw className="w-3.5 h-3.5 animate-spin" />
                      <span>Saving...</span>
                    </>
                  ) : (
                    <>
                      <Save className="w-3.5 h-3.5" />
                      <span>Save Profile Changes</span>
                    </>
                  )}
                </button>
              </div>
            </form>
          </div>

          {/* Card 2: Password & Authentication Security */}
          <div className="p-6 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-4">
            <div className="flex items-center justify-between pb-2 border-b border-[var(--border-subtle)]">
              <div className="flex items-center gap-2.5">
                <Lock className="w-5 h-5 text-purple-400" />
                <div>
                  <h2 className="text-base font-bold text-[var(--text-main)]">
                    Password &amp; Credentials
                  </h2>
                  <p className="text-xs text-[var(--text-muted)]">
                    {user?.hasPassword
                      ? 'Change your existing password for direct username/password login.'
                      : 'Create a master password for your account to sign in without email OTP.'}
                  </p>
                </div>
              </div>

              <span
                className={`px-2.5 py-1 rounded-full text-[10px] font-semibold ${
                  user?.hasPassword
                    ? 'bg-emerald-500/10 text-emerald-400'
                    : 'bg-amber-500/10 text-amber-400'
                }`}
              >
                {user?.hasPassword ? 'Password Configured' : 'OTP Only / No Password'}
              </span>
            </div>

            {passwordSuccess && (
              <div className="p-3 rounded-xl bg-emerald-500/10 border border-emerald-500/20 text-emerald-400 text-xs flex items-center gap-2">
                <Check className="w-4 h-4 shrink-0" />
                <span>{passwordSuccess}</span>
              </div>
            )}

            {passwordError && (
              <div className="p-3 rounded-xl bg-red-500/10 border border-red-500/20 text-red-400 text-xs flex items-center gap-2">
                <AlertTriangle className="w-4 h-4 shrink-0" />
                <span>{passwordError}</span>
              </div>
            )}

            <form onSubmit={handlePasswordSubmit} className="space-y-4">
              {user?.hasPassword && (
                <div className="space-y-1.5">
                  <label className="text-xs font-semibold text-[var(--text-main)]">Current Password</label>
                  <input
                    type="password"
                    required
                    value={currentPassword}
                    onChange={(e) => setCurrentPassword(e.target.value)}
                    placeholder="Enter your current password"
                    className="w-full h-10 px-3.5 rounded-xl bg-[var(--bg-subtle)] text-xs text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)]"
                  />
                </div>
              )}

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                <div className="space-y-1.5">
                  <label className="text-xs font-semibold text-[var(--text-main)]">New Password</label>
                  <input
                    type="password"
                    required
                    minLength={6}
                    value={newPassword}
                    onChange={(e) => setNewPassword(e.target.value)}
                    placeholder="Minimum 6 characters"
                    className="w-full h-10 px-3.5 rounded-xl bg-[var(--bg-subtle)] text-xs text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)]"
                  />
                </div>

                <div className="space-y-1.5">
                  <label className="text-xs font-semibold text-[var(--text-main)]">Confirm New Password</label>
                  <input
                    type="password"
                    required
                    minLength={6}
                    value={confirmPassword}
                    onChange={(e) => setConfirmPassword(e.target.value)}
                    placeholder="Re-type new password"
                    className="w-full h-10 px-3.5 rounded-xl bg-[var(--bg-subtle)] text-xs text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-[var(--brand-ring)]"
                  />
                </div>
              </div>

              <div className="flex justify-end pt-2">
                <button
                  type="submit"
                  disabled={isSavingPassword}
                  className="h-10 px-5 rounded-xl bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)] text-xs font-semibold text-[var(--text-main)] transition-all shadow-sm active:scale-[0.98] flex items-center gap-2 cursor-pointer disabled:opacity-50"
                >
                  {isSavingPassword ? (
                    <>
                      <RefreshCw className="w-3.5 h-3.5 animate-spin" />
                      <span>Updating...</span>
                    </>
                  ) : (
                    <>
                      <Key className="w-3.5 h-3.5" />
                      <span>{user?.hasPassword ? 'Update Password' : 'Set Password'}</span>
                    </>
                  )}
                </button>
              </div>
            </form>
          </div>

          {/* Card 3: Active Security Sessions */}
          <div className="p-6 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-4">
            <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 pb-2 border-b border-[var(--border-subtle)]">
              <div className="flex items-center gap-2.5">
                <Laptop className="w-5 h-5 text-blue-400" />
                <div>
                  <h2 className="text-base font-bold text-[var(--text-main)]">Active Sessions</h2>
                  <p className="text-xs text-[var(--text-muted)]">
                    Devices and sessions with active refresh tokens in the database.
                  </p>
                </div>
              </div>

              {sessions.filter((s) => !s.isCurrent).length > 0 && (
                <button
                  type="button"
                  onClick={handleRevokeOtherSessions}
                  disabled={isRevokingAll}
                  className="h-9 px-3 rounded-xl bg-red-500/10 hover:bg-red-500/20 text-red-400 text-xs font-semibold transition-all cursor-pointer flex items-center gap-1.5"
                >
                  <Trash2 className="w-3.5 h-3.5" />
                  <span>Revoke Other Sessions</span>
                </button>
              )}
            </div>

            {sessionError && (
              <div className="p-3 rounded-xl bg-red-500/10 border border-red-500/20 text-red-400 text-xs flex items-center gap-2">
                <AlertTriangle className="w-4 h-4 shrink-0" />
                <span>{sessionError}</span>
              </div>
            )}

            {isLoadingSessions ? (
              <div className="p-8 text-center text-xs text-[var(--text-muted)]">
                <RefreshCw className="w-5 h-5 animate-spin text-[var(--brand-primary)] mx-auto mb-2" />
                Loading active database sessions...
              </div>
            ) : sessions.length === 0 ? (
              <div className="p-6 rounded-xl bg-[var(--bg-subtle)] text-center text-xs text-[var(--text-muted)]">
                No active refresh sessions found.
              </div>
            ) : (
              <div className="space-y-2">
                {sessions.map((s) => (
                  <div
                    key={s.id}
                    className="p-3.5 rounded-xl bg-[var(--bg-subtle)] flex items-center justify-between gap-3"
                  >
                    <div className="flex items-center gap-3 min-w-0">
                      <div
                        className={`w-8 h-8 rounded-xl flex items-center justify-center shrink-0 ${
                          s.isCurrent
                            ? 'bg-emerald-500/15 text-emerald-400 ring-1 ring-emerald-500/30'
                            : 'bg-[var(--bg-surface-hover)] text-[var(--text-muted)]'
                        }`}
                      >
                        <Globe className="w-4 h-4" />
                      </div>
                      <div className="min-w-0">
                        <div className="text-xs font-semibold text-[var(--text-main)] flex items-center gap-2 truncate">
                          <span>{s.userAgent || 'Web Browser'}</span>
                          {s.isCurrent && (
                            <span className="px-2 py-0.5 rounded-full bg-emerald-500/20 text-emerald-400 text-[10px] font-bold">
                              Current Session
                            </span>
                          )}
                        </div>
                        <div className="text-[11px] text-[var(--text-muted)] flex items-center gap-3 mt-0.5 truncate">
                          <span>IP: {s.ipAddress || 'Localhost / Unknown'}</span>
                          <span>•</span>
                          <span>Created: {new Date(s.createdAt).toLocaleDateString()}</span>
                        </div>
                      </div>
                    </div>

                    {!s.isCurrent && (
                      <button
                        type="button"
                        onClick={() => handleRevokeSession(s.id)}
                        className="p-2 rounded-lg hover:bg-red-500/15 text-[var(--text-muted)] hover:text-red-400 transition-colors cursor-pointer"
                        title="Revoke Session"
                      >
                        <Trash2 className="w-4 h-4" />
                      </button>
                    )}
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>

        {/* Right Column: Roles, Permissions, and Workspaces */}
        <div className="space-y-6">
          {/* Card 4: Assigned Roles & Permissions */}
          <div className="p-6 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-4">
            <div className="flex items-center gap-2 pb-2 border-b border-[var(--border-subtle)]">
              <Shield className="w-5 h-5 text-[var(--brand-primary)]" />
              <div>
                <h3 className="text-sm font-bold text-[var(--text-main)]">Assigned RBAC Roles</h3>
                <p className="text-[11px] text-[var(--text-muted)]">
                  Live privileges granted to your account in this organization.
                </p>
              </div>
            </div>

            <div className="flex flex-wrap gap-1.5 pt-1">
              <span className="px-2.5 py-1 rounded-xl text-xs font-semibold bg-[var(--brand-subtle)] text-[var(--brand-primary)] ring-1 ring-[var(--brand-ring)]">
                {currentOrg.role || 'Member'}
              </span>
              {isSysAdmin && (
                <span className="px-2.5 py-1 rounded-xl text-xs font-semibold bg-purple-500/15 text-purple-400 ring-1 ring-purple-500/30">
                  Platform administrator
                </span>
              )}
            </div>

            <div className="space-y-2 pt-2 border-t border-[var(--border-subtle)]">
              <div className="flex items-center justify-between text-xs">
                <span className="font-semibold text-[var(--text-main)]">Granted Permissions</span>
                <span className="font-mono text-[10px] text-[var(--text-muted)]">
                  {userPermissions.length} active
                </span>
              </div>

              {hasFullWorkspaceAccess ? (
                <div className="p-3 rounded-xl bg-[var(--brand-subtle)] text-[var(--brand-primary)] text-xs">
                  <div className="font-semibold">Full workspace access</div>
                  <p className="text-[10px] opacity-80 mt-0.5">
                    As {currentOrg.role} of {currentOrg.name}, you have every permission in this workspace.
                  </p>
                </div>
              ) : userPermissions.length === 0 ? (
                <p className="text-xs text-[var(--text-muted)] italic">
                  No permissions in this workspace yet. Ask an Owner or Admin to assign a security role.
                </p>
              ) : (
                <div className="max-h-48 overflow-y-auto space-y-1 pr-1">
                  {userPermissions.map((p) => (
                    <div
                      key={p}
                      className="p-2 rounded-lg bg-[var(--bg-subtle)] text-[11px] font-mono flex items-center gap-2 text-[var(--text-secondary)]"
                    >
                      <Check className="w-3 h-3 text-emerald-400 shrink-0" />
                      <span className="truncate">{p}</span>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>

          {/* Card 5: Available Organizations */}
          <div className="p-6 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-md shadow-md space-y-4">
            <div className="flex items-center gap-2 pb-2 border-b border-[var(--border-subtle)]">
              <Building2 className="w-5 h-5 text-amber-400" />
              <div>
                <h3 className="text-sm font-bold text-[var(--text-main)]">Tenant Organizations</h3>
                <p className="text-[11px] text-[var(--text-muted)]">
                  Workspaces accessible with this profile.
                </p>
              </div>
            </div>

            <div className="space-y-2 pt-1">
              {availableOrgs.map((org) => {
                const isCurrent = org.slug === currentOrg.slug;

                return (
                  <div
                    key={org.id}
                    onClick={() => {
                      if (!isCurrent) switchOrg(org.slug);
                    }}
                    className={`p-3 rounded-xl transition-all flex items-center justify-between cursor-pointer ${
                      isCurrent
                        ? 'bg-[var(--brand-subtle)] ring-1 ring-[var(--brand-ring)] text-[var(--text-main)]'
                        : 'bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)] text-[var(--text-secondary)]'
                    }`}
                  >
                    <div className="flex items-center gap-2.5 min-w-0">
                      <div className="w-7 h-7 rounded-lg bg-[var(--bg-surface)] flex items-center justify-center font-bold text-xs shrink-0">
                        {org.name.charAt(0)}
                      </div>
                      <div className="min-w-0">
                        <div className="text-xs font-semibold truncate">{org.name}</div>
                        <div className="text-[10px] text-[var(--text-muted)] truncate">/org/{org.slug}</div>
                      </div>
                    </div>

                    {isCurrent ? (
                      <span className="px-2 py-0.5 rounded-full bg-[var(--brand-primary)] text-white text-[10px] font-bold shrink-0">
                        Active
                      </span>
                    ) : (
                      <span className="text-[11px] text-[var(--brand-primary)] hover:underline shrink-0">
                        Switch &rarr;
                      </span>
                    )}
                  </div>
                );
              })}
            </div>
          </div>
        </div>
      </div>

      <section className="p-4 sm:p-6 rounded-2xl border border-rose-500/30 bg-rose-500/5 space-y-3">
        <div>
          <h2 className="text-sm font-bold text-rose-400">Danger zone</h2>
          <p className="mt-1 text-xs text-[var(--text-secondary)]">
            Deleting your account signs you out and removes you from every workspace. Companies you solely own must be transferred or deleted first. Your past invoices and ledger entries stay on those companies.
          </p>
        </div>
        <button
          type="button"
          onClick={openDeleteDialog}
          className="h-10 px-4 rounded-xl border border-rose-500/40 text-rose-400 text-xs font-semibold hover:bg-rose-500/10"
        >
          Delete account
        </button>
      </section>

      {deleteDialogOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4">
          <form
            ref={deleteDialogRef}
            role="alertdialog"
            aria-modal="true"
            aria-labelledby="delete-account-title"
            aria-describedby="delete-account-description"
            onSubmit={handleDeleteAccount}
            onKeyDown={(event) => {
              if (event.key === 'Escape' && !deletingAccount) {
                event.preventDefault();
                setDeleteDialogOpen(false);
                return;
              }
              if (event.key !== 'Tab') return;
              const focusable = deleteDialogRef.current?.querySelectorAll<HTMLElement>(
                'input:not(:disabled), button:not(:disabled)',
              );
              if (!focusable?.length) return;
              const first = focusable[0];
              const last = focusable[focusable.length - 1];
              if (event.shiftKey && document.activeElement === first) {
                event.preventDefault();
                last.focus();
              } else if (!event.shiftKey && document.activeElement === last) {
                event.preventDefault();
                first.focus();
              }
            }}
            className="w-full max-w-md rounded-2xl border border-[var(--border-subtle)] bg-[var(--bg-surface)] p-6 shadow-2xl space-y-4"
          >
            <div>
              <h2 id="delete-account-title" className="text-base font-bold text-[var(--text-main)]">
                Delete your account?
              </h2>
              <p id="delete-account-description" className="mt-2 text-sm text-[var(--text-secondary)]">
                This closes your login. Type <strong>{user?.email}</strong> to confirm.
              </p>
            </div>
            {loadingBlockers ? (
              <p className="text-xs text-[var(--text-muted)]">Checking workspaces…</p>
            ) : accountDeletionBlocked ? (
              <div className="space-y-2 text-sm text-rose-300">
                {blockers.platformAdministrator && (
                  <p>The last platform administrator cannot delete their account.</p>
                )}
                {blockers.workspaces.length > 0 && (
                  <div>
                    <p>Transfer ownership or delete these workspaces first:</p>
                    <ul className="mt-1 list-disc pl-5">
                      {blockers.workspaces.map((workspace) => (
                        <li key={workspace.slug}>{workspace.name} ({workspace.slug})</li>
                      ))}
                    </ul>
                  </div>
                )}
              </div>
            ) : (
              <>
                <label htmlFor="confirm-account-email" className="block text-xs font-semibold text-[var(--text-secondary)]">
                  Email address
                </label>
                <input
                  id="confirm-account-email"
                  ref={deleteConfirmRef}
                  value={deleteEmail}
                  onChange={(event) => setDeleteEmail(event.target.value)}
                  autoComplete="off"
                  className="w-full h-11 px-3 rounded-xl bg-[var(--bg-subtle)] text-sm text-[var(--text-main)] focus:outline-none focus:ring-2 focus:ring-rose-500"
                />
              </>
            )}
            {deleteError && <p role="alert" className="text-sm text-rose-400">{deleteError}</p>}
            <div className="flex justify-end gap-2">
              <button
                type="button"
                disabled={deletingAccount}
                onClick={() => setDeleteDialogOpen(false)}
                className="h-10 px-4 rounded-xl text-xs font-semibold text-[var(--text-secondary)] hover:bg-[var(--bg-surface-hover)]"
              >
                Cancel
              </button>
              <button
                type="submit"
                disabled={deletingAccount || loadingBlockers || accountDeletionBlocked || deleteEmail.trim().toLowerCase() !== (user?.email || '').toLowerCase()}
                className="h-10 px-4 rounded-xl bg-rose-500 text-white text-xs font-semibold disabled:opacity-40"
              >
                {deletingAccount ? 'Deleting…' : 'Delete account'}
              </button>
            </div>
          </form>
        </div>
      )}
    </div>
  );
};

export default Profile;
