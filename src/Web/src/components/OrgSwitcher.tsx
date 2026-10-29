import React, { useState, useRef, useEffect } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { ChevronsUpDown, Check, Plus, Building2, ExternalLink } from 'lucide-react';
import { OrgDetails } from '../types';

interface OrgSwitcherProps {
  currentOrg: OrgDetails;
  availableOrgs: OrgDetails[];
  collapsed?: boolean;
}

export const OrgSwitcher: React.FC<OrgSwitcherProps> = ({
  currentOrg,
  availableOrgs,
  collapsed = false,
}) => {
  const [isOpen, setIsOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);
  const navigate = useNavigate();
  const location = useLocation();

  // Close dropdown on outside click or Escape key
  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
        setIsOpen(false);
      }
    }
    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        setIsOpen(false);
      }
    }
    document.addEventListener('mousedown', handleClickOutside);
    document.addEventListener('keydown', handleKeyDown);
    return () => {
      document.removeEventListener('mousedown', handleClickOutside);
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, []);

  const handleSelectOrg = (targetSlug: string) => {
    setIsOpen(false);
    if (targetSlug === currentOrg.slug) return;

    // Preserve the current sub-path when switching orgs (e.g. /settings -> /settings)
    const currentSubPath = location.pathname.replace(/^\/org\/[^/]+/, '');
    const targetPath = `/org/${targetSlug}${currentSubPath || '/dashboard'}`;
    navigate(targetPath);
  };

  const getInitials = (name: string) => {
    return name
      .split(' ')
      .map((part) => part[0])
      .filter(Boolean)
      .slice(0, 2)
      .join('')
      .toUpperCase();
  };

  return (
    <div className="relative w-full" ref={containerRef}>
      <button
        type="button"
        onClick={() => setIsOpen(!isOpen)}
        className={`w-full flex items-center gap-3 p-2 rounded-xl transition-all duration-150 text-left cursor-pointer ${
          isOpen
            ? 'bg-[var(--bg-surface-hover)] shadow-lg'
            : 'bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)]'
        } ${collapsed ? 'justify-center p-2' : ''}`}
        title={collapsed ? currentOrg.name : undefined}
        aria-label={collapsed ? `Current workspace: ${currentOrg.name}` : undefined}
        aria-haspopup="menu"
        aria-expanded={isOpen}
      >
        {/* Monogram avatar */}
        <div
          className={`w-8 h-8 rounded-xl flex items-center justify-center font-bold text-xs text-white shadow-sm flex-shrink-0 ${
            currentOrg.avatarBg || 'bg-gradient-to-br from-indigo-500 to-blue-600'
          }`}
        >
          {getInitials(currentOrg.name)}
        </div>

        {!collapsed && (
          <>
            <div className="flex-1 min-w-0">
              <div className="flex items-center gap-1.5">
                <span className="font-semibold text-sm text-[var(--text-main)] truncate">
                  {currentOrg.name}
                </span>
              </div>
              <div className="flex items-center gap-1 text-[11px] text-[var(--text-muted)] font-mono">
                <span className="truncate">{currentOrg.slug}</span>
                {currentOrg.plan && (
                  <span className="ml-1 uppercase text-[9px] px-1.5 py-0.2 rounded-full bg-[var(--bg-subtle)] text-[var(--text-secondary)] font-sans font-medium">
                    {currentOrg.plan}
                  </span>
                )}
              </div>
            </div>
            <ChevronsUpDown className="w-4 h-4 text-[var(--text-muted)] flex-shrink-0" />
          </>
        )}
      </button>

      {/* Dropdown Popover (Border-Less with Ambient Shadow) */}
      {isOpen && (
        <div
          role="menu"
          aria-label="Available Workspaces"
          className={`absolute z-50 rounded-2xl bg-[var(--bg-surface)] backdrop-blur-2xl shadow-2xl shadow-black/80 p-1.5 text-[var(--text-main)] transition-all ring-1 ring-white/10 ${
            collapsed
              ? 'left-full ml-3 top-0 w-64'
              : 'left-0 right-0 top-full mt-1.5 w-full'
          }`}
        >
          <div className="px-3 py-2 text-[10px] font-bold uppercase tracking-wider text-[var(--text-muted)] flex items-center justify-between">
            <span>Workspaces</span>
            <span className="text-[10px] lowercase text-[var(--text-muted)]">
              {availableOrgs.length} org{availableOrgs.length > 1 ? 's' : ''}
            </span>
          </div>

          <div className="space-y-0.5 max-h-60 overflow-y-auto smooth-scroll">
            {availableOrgs.map((org) => {
              const isSelected = org.slug === currentOrg.slug;
              return (
                <button
                  key={org.id}
                  type="button"
                  role="menuitem"
                  onClick={() => handleSelectOrg(org.slug)}
                  className={`w-full flex items-center gap-3 p-2 rounded-xl text-left transition-colors cursor-pointer ${
                    isSelected
                      ? 'bg-[var(--brand-subtle)] text-[var(--text-main)]'
                      : 'hover:bg-[var(--bg-surface-hover)] text-[var(--text-secondary)] hover:text-[var(--text-main)]'
                  }`}
                >
                  <div
                    className={`w-7 h-7 rounded-lg flex items-center justify-center font-bold text-[11px] text-white flex-shrink-0 ${
                      org.avatarBg || 'bg-slate-700'
                    }`}
                  >
                    {getInitials(org.name)}
                  </div>
                  <div className="flex-1 min-w-0">
                    <div className="text-xs font-semibold truncate flex items-center justify-between">
                      <span className={isSelected ? 'text-[var(--brand-primary)] font-bold' : 'text-[var(--text-main)]'}>
                        {org.name}
                      </span>
                    </div>
                    <div className="text-[11px] text-[var(--text-muted)] font-mono truncate">
                      {org.slug}
                    </div>
                  </div>
                  {isSelected && <Check className="w-4 h-4 text-[var(--brand-primary)] flex-shrink-0" />}
                </button>
              );
            })}
          </div>

          <div className="mt-1.5 pt-1.5 space-y-0.5 border-t border-[var(--bg-subtle)]">
            <button
              type="button"
              role="menuitem"
              onClick={() => {
                setIsOpen(false);
                navigate('/select-org');
              }}
              className="w-full flex items-center gap-2.5 px-3 py-2 rounded-xl text-xs font-medium text-[var(--text-secondary)] hover:text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)] transition-colors cursor-pointer"
            >
              <Building2 className="w-3.5 h-3.5 text-[var(--text-muted)]" />
              <span>All Organizations</span>
              <ExternalLink className="w-3 h-3 text-[var(--text-muted)] ml-auto" />
            </button>
            <button
              type="button"
              role="menuitem"
              onClick={() => {
                setIsOpen(false);
                navigate('/create-org');
              }}
              className="w-full flex items-center gap-2.5 px-3 py-2 rounded-xl text-xs font-medium text-[var(--brand-primary)] hover:opacity-90 hover:bg-[var(--brand-subtle)] transition-colors cursor-pointer"
            >
              <Plus className="w-3.5 h-3.5" />
              <span>Create Workspace</span>
            </button>
          </div>
        </div>
      )}
    </div>
  );
};

export default OrgSwitcher;
