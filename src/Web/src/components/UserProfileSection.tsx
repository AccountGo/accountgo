import React, { useState, useRef, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  ChevronsUpDown,
  User,
  LogOut,
  ShieldCheck,
  Palette,
  Sun,
  Moon,
  Laptop,
  Check,
} from 'lucide-react';
import { UserProfile } from '../types';
import { useTheme } from '../themes/ThemeContext';
import { THEMES, ThemeId, SchemeId } from '../themes';
import { useAuth } from '../auth/AuthContext';

interface UserProfileSectionProps {
  user: UserProfile;
  collapsed?: boolean;
  currentOrgSlug?: string;
  onNavigate?: () => void;
}

export const UserProfileSection: React.FC<UserProfileSectionProps> = ({
  user,
  collapsed = false,
  currentOrgSlug,
  onNavigate,
}) => {
  const [open, setOpen] = useState(false);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const navigate = useNavigate();
  const { theme, scheme, setTheme, setScheme } = useTheme();
  const { logout } = useAuth();

  const schemeOptions = [
    { id: 'dark', label: 'Dark', icon: Moon },
    { id: 'light', label: 'Light', icon: Sun },
    { id: 'system', label: 'Auto', icon: Laptop },
  ] as const;

  const getInitials = (name?: string, email?: string) => {
    if (name) {
      const parts = name.trim().split(/\s+/);
      if (parts.length >= 2) {
        return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
      }
      return parts[0].slice(0, 2).toUpperCase();
    }
    if (email) {
      return email.slice(0, 2).toUpperCase();
    }
    return 'AU';
  };

  const initials = getInitials(user.name, user.email);

  // Close menu on outside click or Escape key
  useEffect(() => {
    if (!open) return;

    function handleOutside(e: MouseEvent | TouchEvent) {
      if (
        menuRef.current &&
        !menuRef.current.contains(e.target as Node) &&
        triggerRef.current &&
        !triggerRef.current.contains(e.target as Node)
      ) {
        setOpen(false);
      }
    }

    function handleKey(e: KeyboardEvent) {
      if (e.key === 'Escape') {
        setOpen(false);
        triggerRef.current?.focus();
      }
    }

    document.addEventListener('mousedown', handleOutside);
    document.addEventListener('touchstart', handleOutside, { passive: true });
    document.addEventListener('keydown', handleKey);
    return () => {
      document.removeEventListener('mousedown', handleOutside);
      document.removeEventListener('touchstart', handleOutside);
      document.removeEventListener('keydown', handleKey);
    };
  }, [open]);

  const handleAction = (cb: () => void) => {
    setOpen(false);
    cb();
    onNavigate?.();
  };

  return (
    <div className="relative w-full">
      {/* Trigger Button */}
      <button
        ref={triggerRef}
        type="button"
        onClick={() => setOpen((prev) => !prev)}
        aria-expanded={open}
        aria-haspopup="menu"
        aria-label={collapsed ? user.name || user.email : undefined}
        title={collapsed ? user.name || user.email : undefined}
        className={`w-full flex items-center gap-3 p-2 rounded-xl transition-all duration-150 text-left cursor-pointer ${open
            ? 'bg-[var(--bg-surface-hover)] shadow-lg'
            : 'bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)]'
          } ${collapsed ? 'justify-center p-2' : ''}`}
      >
        {/* User Avatar with Online Dot */}
        <div className="relative flex-shrink-0">
          <div className="w-8 h-8 rounded-xl bg-gradient-to-br from-[var(--brand-primary)] to-emerald-800 flex items-center justify-center font-bold text-xs text-[var(--brand-text)] shadow-sm ring-1 ring-[var(--brand-ring)]">
            {initials}
          </div>
          <span
            aria-label="Online"
            className="absolute -bottom-0.5 -right-0.5 w-2.5 h-2.5 rounded-full bg-[var(--brand-primary)] ring-2 ring-[var(--bg-canvas)]"
          />
        </div>

        {/* User Info (hidden when collapsed) */}
        {!collapsed && (
          <>
            <div className="flex-1 min-w-0">
              <div className="text-xs font-semibold text-[var(--text-main)] truncate leading-tight">
                {user.name || 'Admin User'}
              </div>
              <div className="text-[11px] text-[var(--text-muted)] truncate leading-tight mt-0.5">
                {user.email || 'admin@accountgo.ph'}
              </div>
            </div>
            <ChevronsUpDown className="w-4 h-4 text-[var(--text-muted)] flex-shrink-0" />
          </>
        )}
      </button>

      {/* Popover Menu (Border-Less with Ambient Shadow) */}
      {open && (
        <div
          ref={menuRef}
          role="menu"
          aria-label="User profile menu"
          className={`absolute z-50 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-2xl shadow-2xl shadow-black/80 p-1.5 text-[var(--text-main)] transition-all ring-1 ring-white/10 ${collapsed
              ? 'left-full ml-3 bottom-0 w-72'
              : 'left-0 right-0 bottom-full mb-2 w-full'
            }`}
        >
          {/* Header Info Card */}
          <div className="p-3 flex items-center gap-3">
            <div className="w-9 h-9 rounded-xl bg-gradient-to-br from-[var(--brand-primary)] to-emerald-800 flex items-center justify-center font-bold text-xs text-[var(--brand-text)] shadow-sm ring-1 ring-[var(--brand-ring)] flex-shrink-0">
              {initials}
            </div>
            <div className="flex-1 min-w-0">
              <div className="text-xs font-semibold text-[var(--text-main)] truncate flex items-center gap-1.5">
                <span>{user.name}</span>
                <ShieldCheck className="w-3.5 h-3.5 text-[var(--brand-primary)] flex-shrink-0" />
              </div>
              <div className="text-[11px] text-[var(--text-muted)] truncate mt-0.5">
                {user.email}
              </div>
              <div className="mt-1">
                <span className="text-[9px] uppercase font-mono px-2 py-0.5 rounded-full bg-[var(--brand-subtle)] text-[var(--brand-primary)] font-semibold">
                  {user.role || 'System Administrator'}
                </span>
              </div>
            </div>
          </div>

          <div className="h-px bg-[var(--bg-subtle)] my-1 mx-2" />

          {/* Menu Action Items - Personal User Scope */}
          <div className="p-1 space-y-1">
            <button
              type="button"
              role="menuitem"
              onClick={() =>
                handleAction(() => {
                  if (currentOrgSlug) {
                    navigate(`/org/${currentOrgSlug}/profile`);
                  }
                })
              }
              className="w-full flex items-center gap-2.5 px-3 py-2 rounded-xl text-xs font-medium text-[var(--text-secondary)] hover:text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)] transition-colors text-left cursor-pointer"
            >
              <User className="w-3.5 h-3.5 text-[var(--text-muted)]" />
              <span>Personal Profile</span>
            </button>

            {/* Compact Appearance & Theme Toggle Panel */}
            <div className="p-2 rounded-xl bg-[var(--bg-subtle)] space-y-2">
              <div className="flex items-center justify-between text-[10px] font-bold uppercase tracking-wider text-[var(--text-muted)] px-0.5">
                <span className="flex items-center gap-1.5">
                  <Palette className="w-3 h-3 text-[var(--brand-primary)]" />
                  <span>Mode</span>
                </span>
                <span className="capitalize text-[var(--brand-primary)] font-mono text-[9px]">
                  {scheme}
                </span>
              </div>

              {/* Compact 3-way Segmented Scheme Toggle */}
              <div className="grid grid-cols-3 gap-1 p-0.5 rounded-lg bg-[var(--bg-surface)] text-[11px] font-medium">
                {schemeOptions.map((opt) => {
                  const active = scheme === opt.id;
                  const Icon = opt.icon;
                  return (
                    <button
                      key={opt.id}
                      type="button"
                      onClick={() => setScheme(opt.id as SchemeId)}
                      className={`flex items-center justify-center gap-1 py-1 px-1.5 rounded-md transition-all cursor-pointer ${active
                          ? 'bg-[var(--brand-primary)] text-[var(--brand-text)] font-semibold shadow-xs'
                          : 'text-[var(--text-muted)] hover:text-[var(--text-main)]'
                        }`}
                      title={`${opt.label} mode`}
                    >
                      <Icon className="w-3 h-3" />
                      <span className="text-[10px]">{opt.label}</span>
                    </button>
                  );
                })}
              </div>

              {/* Compact Theme Palette Dots with Tooltips */}
              <div className="pt-0.5">
                <div className="flex items-center justify-between text-[10px] font-bold uppercase tracking-wider text-[var(--text-muted)] px-0.5 mb-1.5">
                  <span>Theme</span>
                  <span className="text-[10px] font-medium text-[var(--text-main)] truncate max-w-[120px]">
                    {THEMES.find((t) => t.id === theme)?.label.replace('AccountGo ', '')}
                  </span>
                </div>
                <div className="flex items-center justify-between gap-1 px-0.5">
                  {THEMES.map((t) => {
                    const isSelected = theme === t.id;
                    return (
                      <button
                        key={t.id}
                        type="button"
                        onClick={() => setTheme(t.id as ThemeId)}
                        className={`group relative flex items-center justify-center p-1 rounded-lg transition-all cursor-pointer ${isSelected
                            ? 'ring-2 ring-[var(--brand-primary)] bg-[var(--brand-subtle)]'
                            : 'hover:bg-[var(--bg-surface-hover)]'
                          }`}
                        title={t.label}
                      >
                        <span
                          className="w-4 h-4 rounded-md shadow-xs transition-transform group-hover:scale-110 flex items-center justify-center"
                          style={{ backgroundColor: t.previewColor }}
                        >
                          {isSelected && <Check className="w-2.5 h-2.5 text-white stroke-[3]" />}
                        </span>
                      </button>
                    );
                  })}
                </div>
              </div>
            </div>

            <div className="h-px bg-[var(--bg-subtle)] my-1 mx-1" />

            <button
              type="button"
              role="menuitem"
              onClick={() =>
                handleAction(async () => {
                  await logout();
                  navigate('/login');
                })
              }
              className="w-full flex items-center gap-2.5 px-3 py-2 rounded-xl text-xs font-medium text-rose-400 hover:text-rose-300 hover:bg-rose-500/10 transition-colors text-left cursor-pointer"
            >
              <LogOut className="w-3.5 h-3.5" />
              <span>Sign out</span>
            </button>
          </div>
        </div>
      )}
    </div>
  );
};

export default UserProfileSection;
