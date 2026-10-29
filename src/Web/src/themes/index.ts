export const SCHEMES = [
  { id: 'dark', label: 'Dark' },
  { id: 'light', label: 'Light' },
  { id: 'system', label: 'System' },
] as const;

export type SchemeId = (typeof SCHEMES)[number]['id'];
export type ResolvedSchemeId = 'dark' | 'light';

export interface ThemeColors {
  bgCanvas: string;
  bgSurface: string;
  bgSurfaceHover: string;
  bgSubtle: string;
  bgSidebar: string;
  bgHeader: string;
  textMain: string;
  textSecondary: string;
  textMuted: string;
  brandPrimary: string;
  brandHover: string;
  brandText: string;
  brandSubtle: string;
  brandRing: string;
  accent: string;
}

export interface ThemeDefinition {
  id: string;
  label: string;
  description: string;
  previewColor: string;
  colors: {
    dark: ThemeColors;
    light: ThemeColors;
  };
}

export const THEMES: readonly ThemeDefinition[] = [
  {
    id: 'accountgo',
    label: 'AccountGo Emerald',
    description: 'Clean mint emerald brand on deep obsidian or crisp snow',
    previewColor: '#10b981',
    colors: {
      dark: {
        bgCanvas: '#080b12',
        bgSurface: '#0f172a',
        bgSurfaceHover: '#1e293b',
        bgSubtle: '#05070e',
        bgSidebar: 'rgba(2, 6, 23, 0.75)',
        bgHeader: 'rgba(2, 6, 23, 0.50)',
        textMain: '#f8fafc',
        textSecondary: '#94a3b8',
        textMuted: '#64748b',
        brandPrimary: '#10b981',
        brandHover: '#059669',
        brandText: '#022c22',
        brandSubtle: 'rgba(16, 185, 129, 0.12)',
        brandRing: 'rgba(16, 185, 129, 0.35)',
        accent: '#38bdf8',
      },
      light: {
        bgCanvas: '#f8fafc',
        bgSurface: '#ffffff',
        bgSurfaceHover: '#f1f5f9',
        bgSubtle: '#e2e8f0',
        bgSidebar: 'rgba(255, 255, 255, 0.85)',
        bgHeader: 'rgba(255, 255, 255, 0.75)',
        textMain: '#0f172a',
        textSecondary: '#475569',
        textMuted: '#64748b',
        brandPrimary: '#059669',
        brandHover: '#047857',
        brandText: '#ffffff',
        brandSubtle: 'rgba(5, 150, 105, 0.10)',
        brandRing: 'rgba(5, 150, 105, 0.25)',
        accent: '#0284c7',
      },
    },
  },
  {
    id: 'tokyo-night',
    label: 'Tokyo Night',
    description: 'Electric neon indigo and cyber cyan aesthetic',
    previewColor: '#7aa2f7',
    colors: {
      dark: {
        bgCanvas: '#13141c',
        bgSurface: '#1a1b26',
        bgSurfaceHover: '#24283b',
        bgSubtle: '#0e0f15',
        bgSidebar: 'rgba(20, 21, 31, 0.80)',
        bgHeader: 'rgba(20, 21, 31, 0.55)',
        textMain: '#c0caf5',
        textSecondary: '#a9b1d6',
        textMuted: '#565f89',
        brandPrimary: '#7aa2f7',
        brandHover: '#89b4fa',
        brandText: '#13141c',
        brandSubtle: 'rgba(122, 162, 247, 0.14)',
        brandRing: 'rgba(122, 162, 247, 0.35)',
        accent: '#7dcfff',
      },
      light: {
        bgCanvas: '#d5d7e0',
        bgSurface: '#e1e2e7',
        bgSurfaceHover: '#d1d3db',
        bgSubtle: '#c6c8d2',
        bgSidebar: 'rgba(225, 226, 231, 0.88)',
        bgHeader: 'rgba(225, 226, 231, 0.75)',
        textMain: '#1a2b5c',
        textSecondary: '#3760bf',
        textMuted: '#6172b0',
        brandPrimary: '#2e7de9',
        brandHover: '#2069c9',
        brandText: '#ffffff',
        brandSubtle: 'rgba(46, 125, 233, 0.12)',
        brandRing: 'rgba(46, 125, 233, 0.25)',
        accent: '#007197',
      },
    },
  },
  {
    id: 'sapphire',
    label: 'Sapphire Slate',
    description: 'Deep royal blue corporate institutional palette',
    previewColor: '#3b82f6',
    colors: {
      dark: {
        bgCanvas: '#090d16',
        bgSurface: '#0f172a',
        bgSurfaceHover: '#1e293b',
        bgSubtle: '#060910',
        bgSidebar: 'rgba(11, 17, 32, 0.80)',
        bgHeader: 'rgba(11, 17, 32, 0.55)',
        textMain: '#f8fafc',
        textSecondary: '#94a3b8',
        textMuted: '#64748b',
        brandPrimary: '#3b82f6',
        brandHover: '#2563eb',
        brandText: '#ffffff',
        brandSubtle: 'rgba(59, 130, 246, 0.14)',
        brandRing: 'rgba(59, 130, 246, 0.35)',
        accent: '#60a5fa',
      },
      light: {
        bgCanvas: '#f8fafc',
        bgSurface: '#ffffff',
        bgSurfaceHover: '#f1f5f9',
        bgSubtle: '#e2e8f0',
        bgSidebar: 'rgba(255, 255, 255, 0.88)',
        bgHeader: 'rgba(255, 255, 255, 0.75)',
        textMain: '#0f172a',
        textSecondary: '#475569',
        textMuted: '#64748b',
        brandPrimary: '#2563eb',
        brandHover: '#1d4ed8',
        brandText: '#ffffff',
        brandSubtle: 'rgba(37, 99, 235, 0.10)',
        brandRing: 'rgba(37, 99, 235, 0.25)',
        accent: '#1e40af',
      },
    },
  },
  {
    id: 'amber',
    label: 'Amber Bronze',
    description: 'Warm obsidian and executive bronze styling',
    previewColor: '#f59e0b',
    colors: {
      dark: {
        bgCanvas: '#110d08',
        bgSurface: '#1c160f',
        bgSurfaceHover: '#292015',
        bgSubtle: '#0a0805',
        bgSidebar: 'rgba(20, 15, 10, 0.82)',
        bgHeader: 'rgba(20, 15, 10, 0.58)',
        textMain: '#fef3c7',
        textSecondary: '#d6c7b2',
        textMuted: '#78716c',
        brandPrimary: '#f59e0b',
        brandHover: '#d97706',
        brandText: '#451a03',
        brandSubtle: 'rgba(245, 158, 11, 0.14)',
        brandRing: 'rgba(245, 158, 11, 0.35)',
        accent: '#fbbf24',
      },
      light: {
        bgCanvas: '#faf8f5',
        bgSurface: '#ffffff',
        bgSurfaceHover: '#f5f0e9',
        bgSubtle: '#eee7de',
        bgSidebar: 'rgba(255, 255, 255, 0.88)',
        bgHeader: 'rgba(255, 255, 255, 0.75)',
        textMain: '#292524',
        textSecondary: '#57534e',
        textMuted: '#78716c',
        brandPrimary: '#d97706',
        brandHover: '#b45309',
        brandText: '#ffffff',
        brandSubtle: 'rgba(217, 119, 6, 0.10)',
        brandRing: 'rgba(217, 119, 6, 0.25)',
        accent: '#92400e',
      },
    },
  },
  {
    id: 'phosphor',
    label: 'Phosphor Green',
    description: 'Minimalist high-contrast matrix green palette',
    previewColor: '#4ade80',
    colors: {
      dark: {
        bgCanvas: '#000000',
        bgSurface: '#0a0a0a',
        bgSurfaceHover: '#171717',
        bgSubtle: '#040404',
        bgSidebar: 'rgba(8, 8, 8, 0.85)',
        bgHeader: 'rgba(8, 8, 8, 0.60)',
        textMain: '#e0e0e0',
        textSecondary: '#a3a3a3',
        textMuted: '#525252',
        brandPrimary: '#4ade80',
        brandHover: '#22c55e',
        brandText: '#052e16',
        brandSubtle: 'rgba(74, 222, 128, 0.14)',
        brandRing: 'rgba(74, 222, 128, 0.35)',
        accent: '#00e5ff',
      },
      light: {
        bgCanvas: '#ffffff',
        bgSurface: '#f5f5f5',
        bgSurfaceHover: '#e5e5e5',
        bgSubtle: '#d4d4d4',
        bgSidebar: 'rgba(245, 245, 245, 0.90)',
        bgHeader: 'rgba(245, 245, 245, 0.80)',
        textMain: '#171717',
        textSecondary: '#404040',
        textMuted: '#737373',
        brandPrimary: '#16a34a',
        brandHover: '#15803d',
        brandText: '#ffffff',
        brandSubtle: 'rgba(22, 163, 74, 0.10)',
        brandRing: 'rgba(22, 163, 74, 0.25)',
        accent: '#0284c7',
      },
    },
  },
] as const;

export type ThemeId = (typeof THEMES)[number]['id'];

export const DEFAULT_THEME: ThemeId = 'accountgo';
export const DEFAULT_SCHEME: SchemeId = 'dark';
export const THEME_STORAGE_KEY = 'accountgo:theme';
export const SCHEME_STORAGE_KEY = 'accountgo:scheme';

export function isThemeId(value: unknown): value is ThemeId {
  return typeof value === 'string' && THEMES.some((t) => t.id === value);
}

export function isSchemeId(value: unknown): value is SchemeId {
  return typeof value === 'string' && SCHEMES.some((s) => s.id === value);
}

export function getTheme(id: ThemeId): ThemeDefinition {
  return THEMES.find((t) => t.id === id) || THEMES[0];
}

export function themeLabel(id: ThemeId): string {
  return getTheme(id).label;
}

export function schemeLabel(id: SchemeId): string {
  return SCHEMES.find((s) => s.id === id)?.label ?? 'Dark';
}

export function nextTheme(current: ThemeId): ThemeId {
  const index = THEMES.findIndex((t) => t.id === current);
  const next = THEMES[(index + 1) % THEMES.length];
  return next?.id ?? DEFAULT_THEME;
}

export function nextScheme(current: SchemeId): SchemeId {
  const index = SCHEMES.findIndex((s) => s.id === current);
  const next = SCHEMES[(index + 1) % SCHEMES.length];
  return next?.id ?? DEFAULT_SCHEME;
}
