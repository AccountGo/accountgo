import React, { useEffect, useRef } from 'react';
import {
  Sun,
  Moon,
  Laptop,
  Check,
  X,
  Palette,
  Sparkles,
} from 'lucide-react';
import { THEMES, SCHEMES, ThemeId, SchemeId } from '../themes';
import { useTheme } from '../themes/ThemeContext';

interface AppearanceModalProps {
  isOpen: boolean;
  onClose: () => void;
  tenantName?: string;
}

export const AppearanceModal: React.FC<AppearanceModalProps> = ({
  isOpen,
  onClose,
  tenantName,
}) => {
  const { theme, scheme, resolvedScheme, setTheme, setScheme } = useTheme();
  const modalRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!isOpen) return;

    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };

    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [isOpen, onClose]);

  if (!isOpen) return null;

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/60 backdrop-blur-md animate-in fade-in duration-200"
      onClick={onClose}
    >
      <div
        ref={modalRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="appearance-modal-title"
        onClick={(e) => e.stopPropagation()}
        className="w-full max-w-lg rounded-3xl bg-[var(--bg-surface)] text-[var(--text-main)] shadow-2xl shadow-black/80 p-6 space-y-6 max-h-[90vh] overflow-y-auto smooth-scroll animate-in zoom-in-95 duration-150"
      >
        {/* Header */}
        <div className="flex items-center justify-between pb-2">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-2xl bg-[var(--brand-subtle)] text-[var(--brand-primary)] flex items-center justify-center shadow-sm">
              <Palette className="w-5 h-5" />
            </div>
            <div>
              <h2 id="appearance-modal-title" className="text-base sm:text-lg font-bold text-[var(--text-main)]">
                Appearance & Themes
              </h2>
              <p className="text-xs text-[var(--text-muted)]">
                {tenantName ? `Personalized for ${tenantName}` : 'Custom theme & color mode'}
              </p>
            </div>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="p-2 rounded-xl text-[var(--text-muted)] hover:text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)] transition-colors min-h-[44px] min-w-[44px] flex items-center justify-center cursor-pointer"
            aria-label="Close dialog"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Section 1: Color Scheme (Compact Segmented Toggle) */}
        <div className="space-y-2">
          <label className="text-[11px] font-bold uppercase tracking-wider text-[var(--text-muted)]">
            Color Scheme
          </label>
          <div className="grid grid-cols-3 gap-1.5 p-1 rounded-xl bg-[var(--bg-subtle)]">
            {SCHEMES.map((s) => {
              const active = scheme === s.id;
              const Icon = s.id === 'dark' ? Moon : s.id === 'light' ? Sun : Laptop;
              return (
                <button
                  key={s.id}
                  type="button"
                  onClick={() => setScheme(s.id as SchemeId)}
                  className={`flex items-center justify-center gap-2 h-9 rounded-lg text-xs font-semibold transition-all cursor-pointer ${
                    active
                      ? 'bg-[var(--brand-primary)] text-[var(--brand-text)] shadow-sm'
                      : 'text-[var(--text-muted)] hover:text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)]'
                  }`}
                >
                  <Icon className="w-3.5 h-3.5" />
                  <span>{s.label}</span>
                </button>
              );
            })}
          </div>
        </div>

        {/* Section 2: Whitelabel Brand Theme Palettes (Compact Rows) */}
        <div className="space-y-2">
          <div className="flex items-center justify-between">
            <label className="text-[11px] font-bold uppercase tracking-wider text-[var(--text-muted)]">
              Brand Theme Palette
            </label>
            <span className="text-[10px] text-[var(--text-muted)]">
              Mode: <span className="capitalize font-semibold text-[var(--brand-primary)]">{resolvedScheme}</span>
            </span>
          </div>

          <div className="space-y-1.5">
            {THEMES.map((t) => {
              const isSelected = theme === t.id;
              const palette = t.colors[resolvedScheme];

              return (
                <button
                  key={t.id}
                  type="button"
                  onClick={() => setTheme(t.id as ThemeId)}
                  className={`w-full flex items-center justify-between p-2.5 rounded-xl transition-all text-left cursor-pointer ${
                    isSelected
                      ? 'bg-[var(--brand-subtle)] ring-1.5 ring-[var(--brand-primary)]'
                      : 'bg-[var(--bg-subtle)] hover:bg-[var(--bg-surface-hover)]'
                  }`}
                >
                  <div className="flex items-center gap-2.5 min-w-0">
                    {/* Live Palette Swatches */}
                    <div className="flex items-center -space-x-1 flex-shrink-0">
                      <div
                        className="w-4 h-4 rounded-full ring-1.5 ring-[var(--bg-surface)] shadow-xs"
                        style={{ backgroundColor: palette.brandPrimary }}
                        title="Brand Primary"
                      />
                      <div
                        className="w-4 h-4 rounded-full ring-1.5 ring-[var(--bg-surface)] shadow-xs"
                        style={{ backgroundColor: palette.bgSurface }}
                        title="Surface Background"
                      />
                      <div
                        className="w-4 h-4 rounded-full ring-1.5 ring-[var(--bg-surface)] shadow-xs"
                        style={{ backgroundColor: palette.accent }}
                        title="Accent"
                      />
                    </div>

                    <div className="min-w-0">
                      <div className="text-xs font-bold text-[var(--text-main)] flex items-center gap-1.5">
                        <span>{t.label}</span>
                        {t.id === 'accountgo' && (
                          <span className="text-[8px] font-mono uppercase px-1.5 py-0.2 rounded-full bg-[var(--brand-primary)] text-[var(--brand-text)] font-semibold">
                            Default
                          </span>
                        )}
                      </div>
                      <p className="text-[10px] text-[var(--text-muted)] truncate mt-0.2">
                        {t.description}
                      </p>
                    </div>
                  </div>

                  {isSelected && (
                    <div className="w-5 h-5 rounded-full bg-[var(--brand-primary)] text-[var(--brand-text)] flex items-center justify-center flex-shrink-0 ml-2">
                      <Check className="w-3 h-3 stroke-[3]" />
                    </div>
                  )}
                </button>
              );
            })}
          </div>
        </div>

        {/* Live Token Preview Card */}
        <div className="p-4 rounded-2xl bg-[var(--bg-subtle)] space-y-3">
          <div className="flex items-center gap-2 text-xs font-semibold text-[var(--text-secondary)]">
            <Sparkles className="w-4 h-4 text-[var(--brand-primary)]" />
            <span>Live Whitelabel Rendering Preview</span>
          </div>

          <div className="flex flex-wrap items-center gap-2 pt-1">
            <button type="button" className="btn-primary text-xs !h-9 !px-3">
              Primary Action
            </button>
            <button type="button" className="btn-secondary text-xs !h-9 !px-3">
              Secondary
            </button>
            <span className="text-xs font-mono px-2.5 py-1 rounded-lg bg-[var(--brand-subtle)] text-[var(--brand-primary)] font-semibold">
              Tag: {theme}
            </span>
          </div>
        </div>

        {/* Close Button */}
        <div className="pt-2">
          <button
            type="button"
            onClick={onClose}
            className="w-full h-11 rounded-xl bg-[var(--brand-primary)] hover:bg-[var(--brand-hover)] text-[var(--brand-text)] font-bold text-xs transition-all shadow-md cursor-pointer"
          >
            Apply & Close
          </button>
        </div>
      </div>
    </div>
  );
};

export default AppearanceModal;
