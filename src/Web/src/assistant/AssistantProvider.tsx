import React, {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';
import { useAuth } from '../auth/AuthContext';
import { SidebarMode } from '../types';
import { setAssistantAuthFetch } from './assistantFetch';
import { assistantAdapter } from './AssistantAdapter';
import type { AssistantPageContext } from './contextVariables';

const OPEN_STORAGE_KEY = 'accountgo:assistant-open';

type AssistantContextValue = {
  isOpen: boolean;
  open: () => void;
  close: () => void;
  toggle: () => void;
  compactHost: boolean;
  setCompactHost: (value: boolean) => void;
  setPageContext: (context: AssistantPageContext) => void;
};

const AssistantContext = createContext<AssistantContextValue | null>(null);

function readStoredOpen(): boolean {
  if (typeof window === 'undefined') return false;
  try {
    return window.localStorage.getItem(OPEN_STORAGE_KEY) === '1';
  } catch {
    return false;
  }
}

function writeStoredOpen(isOpen: boolean): void {
  try {
    window.localStorage.setItem(OPEN_STORAGE_KEY, isOpen ? '1' : '0');
  } catch {
    /* ignore */
  }
}

type AssistantProviderProps = {
  children: React.ReactNode;
  sidebarMode: SidebarMode;
  setSidebarMode: (mode: SidebarMode) => void;
};

/**
 * App-wide open/close state for the assistant side panel. Lives above Header and
 * ChatProvider so the header button can toggle the panel. On open, collapses the
 * AccountGo sidebar (P0) and restores the prior mode on close.
 */
export function AssistantProvider({
  children,
  sidebarMode,
  setSidebarMode,
}: AssistantProviderProps) {
  const { authFetch } = useAuth();
  const [isOpen, setIsOpen] = useState(readStoredOpen);
  const [compactHost, setCompactHost] = useState(false);
  const sidebarBeforeOpenRef = useRef<SidebarMode | null>(null);
  const sidebarModeRef = useRef(sidebarMode);
  sidebarModeRef.current = sidebarMode;

  // Wire synchronously during render as well as in useEffect so child ChatProvider
  // effects (e.g. initial listSessions() on mount) never observe an uninitialized fetch.
  setAssistantAuthFetch(authFetch);

  useEffect(() => {
    setAssistantAuthFetch(authFetch);
  }, [authFetch]);

  useEffect(() => {
    writeStoredOpen(isOpen);
  }, [isOpen]);

  const setPageContext = useCallback((context: AssistantPageContext) => {
    assistantAdapter.setPageContext(context);
  }, []);

  const open = useCallback(() => {
    if (sidebarModeRef.current !== 'collapsed') {
      sidebarBeforeOpenRef.current = sidebarModeRef.current;
      setSidebarMode('collapsed');
    } else if (sidebarBeforeOpenRef.current == null) {
      sidebarBeforeOpenRef.current = 'collapsed';
    }
    setIsOpen(true);
  }, [setSidebarMode]);

  const close = useCallback(() => {
    setIsOpen(false);
    const previous = sidebarBeforeOpenRef.current;
    sidebarBeforeOpenRef.current = null;
    if (previous && previous !== 'collapsed') {
      setSidebarMode(previous);
    }
  }, [setSidebarMode]);

  const toggle = useCallback(() => {
    if (isOpen) {
      close();
    } else {
      open();
    }
  }, [close, isOpen, open]);

  const value = useMemo(
    () => ({
      isOpen,
      open,
      close,
      toggle,
      compactHost,
      setCompactHost,
      setPageContext,
    }),
    [isOpen, open, close, toggle, compactHost, setPageContext],
  );

  return <AssistantContext.Provider value={value}>{children}</AssistantContext.Provider>;
}

export function useAssistant(): AssistantContextValue {
  const context = useContext(AssistantContext);
  if (!context) {
    throw new Error('useAssistant must be used within AssistantProvider');
  }
  return context;
}
