import React, { createContext, useContext, useEffect, useState, useMemo, useCallback } from 'react';
import {
  ThemeId,
  SchemeId,
  ResolvedSchemeId,
  ThemeColors,
  DEFAULT_THEME,
  DEFAULT_SCHEME,
  THEME_STORAGE_KEY,
  SCHEME_STORAGE_KEY,
  getTheme,
  isThemeId,
  isSchemeId,
  nextTheme,
  nextScheme,
} from './index';
import { WhitelabelConfig } from '../types';

interface ThemeContextValue {
  theme: ThemeId;
  scheme: SchemeId;
  resolvedScheme: ResolvedSchemeId;
  colors: ThemeColors;
  setTheme: (theme: ThemeId) => void;
  setScheme: (scheme: SchemeId) => void;
  cycleTheme: () => void;
  cycleScheme: () => void;
  setTenantWhitelabel: (config?: WhitelabelConfig | null) => void;
  tenantWhitelabel: WhitelabelConfig | null;
}

const ThemeContext = createContext<ThemeContextValue | undefined>(undefined);

function getSystemScheme(): ResolvedSchemeId {
  if (typeof window === 'undefined') return 'dark';
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

function getStoredTheme(): ThemeId {
  if (typeof window === 'undefined') return DEFAULT_THEME;
  try {
    const stored = localStorage.getItem(THEME_STORAGE_KEY);
    if (isThemeId(stored)) return stored;
  } catch {
    // Storage restricted
  }
  return DEFAULT_THEME;
}

function getStoredScheme(): SchemeId {
  if (typeof window === 'undefined') return DEFAULT_SCHEME;
  try {
    const stored = localStorage.getItem(SCHEME_STORAGE_KEY);
    if (isSchemeId(stored)) return stored;
  } catch {
    // Storage restricted
  }
  return DEFAULT_SCHEME;
}

export const ThemeProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [userTheme, setUserThemeState] = useState<ThemeId>(getStoredTheme);
  const [userScheme, setUserSchemeState] = useState<SchemeId>(getStoredScheme);
  const [tenantWhitelabel, setTenantWhitelabelState] = useState<WhitelabelConfig | null>(null);
  const [systemScheme, setSystemScheme] = useState<ResolvedSchemeId>(getSystemScheme);

  const setTenantWhitelabel = useCallback((config?: WhitelabelConfig | null) => {
    setTenantWhitelabelState(config ?? null);
  }, []);

  // Active theme: tenant whitelabel theme if specified, otherwise user preference
  const theme: ThemeId = useMemo(() => {
    if (tenantWhitelabel?.themeId && isThemeId(tenantWhitelabel.themeId)) {
      return tenantWhitelabel.themeId;
    }
    return userTheme;
  }, [tenantWhitelabel, userTheme]);

  // Active scheme: tenant default if specified and user hasn't overridden, or user preference
  const scheme: SchemeId = useMemo(() => {
    return userScheme;
  }, [userScheme]);

  // Listen to OS dark/light mode changes
  useEffect(() => {
    if (typeof window === 'undefined') return;
    const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)');
    const handler = (e: MediaQueryListEvent) => {
      setSystemScheme(e.matches ? 'dark' : 'light');
    };
    mediaQuery.addEventListener('change', handler);
    return () => mediaQuery.removeEventListener('change', handler);
  }, []);

  const resolvedScheme: ResolvedSchemeId = useMemo(() => {
    if (scheme === 'system') return systemScheme;
    return scheme;
  }, [scheme, systemScheme]);

  const colors = useMemo(() => {
    const themeDef = getTheme(theme);
    return themeDef.colors[resolvedScheme];
  }, [theme, resolvedScheme]);

  // Apply data-theme, data-scheme, meta theme-color, and custom whitelabel colors to the DOM
  useEffect(() => {
    const root = document.documentElement;
    root.dataset.theme = theme;
    root.dataset.scheme = resolvedScheme;
    root.style.colorScheme = resolvedScheme;

    // Apply custom brand primary override if tenant provides one
    if (tenantWhitelabel?.primaryColor) {
      root.style.setProperty('--brand-primary', tenantWhitelabel.primaryColor);
      root.style.setProperty('--brand-hover', tenantWhitelabel.primaryColor);
      root.style.setProperty('--brand-subtle', `${tenantWhitelabel.primaryColor}1a`);
      root.style.setProperty('--brand-ring', `${tenantWhitelabel.primaryColor}40`);
    } else {
      root.style.removeProperty('--brand-primary');
      root.style.removeProperty('--brand-hover');
      root.style.removeProperty('--brand-subtle');
      root.style.removeProperty('--brand-ring');
    }

    // Dynamic browser chrome meta theme-color
    const metaThemeColor = document.querySelector('meta[name="theme-color"]');
    if (metaThemeColor) {
      metaThemeColor.setAttribute('content', colors.bgCanvas);
    }
  }, [theme, resolvedScheme, colors, tenantWhitelabel]);

  const setTheme = useCallback((newTheme: ThemeId) => {
    setUserThemeState(newTheme);
    try {
      localStorage.setItem(THEME_STORAGE_KEY, newTheme);
    } catch {
      // Storage restricted
    }
  }, []);

  const setScheme = useCallback((newScheme: SchemeId) => {
    setUserSchemeState(newScheme);
    try {
      localStorage.setItem(SCHEME_STORAGE_KEY, newScheme);
    } catch {
      // Storage restricted
    }
  }, []);

  const cycleTheme = useCallback(() => {
    setTheme(nextTheme(theme));
  }, [theme, setTheme]);

  const cycleScheme = useCallback(() => {
    setScheme(nextScheme(scheme));
  }, [scheme, setScheme]);

  const value = useMemo(
    () => ({
      theme,
      scheme,
      resolvedScheme,
      colors,
      setTheme,
      setScheme,
      cycleTheme,
      cycleScheme,
      setTenantWhitelabel,
      tenantWhitelabel,
    }),
    [
      theme,
      scheme,
      resolvedScheme,
      colors,
      setTheme,
      setScheme,
      cycleTheme,
      cycleScheme,
      setTenantWhitelabel,
      tenantWhitelabel,
    ]
  );

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
};

export function useTheme(): ThemeContextValue {
  const context = useContext(ThemeContext);
  if (!context) {
    throw new Error('useTheme must be used within a ThemeProvider');
  }
  return context;
}
