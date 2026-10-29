import { useEffect, useMemo, useState } from 'react';
import { useLocation, useParams } from 'react-router-dom';
import { ChatProvider, ChatSidepanelLayout } from '@anter/ai-chat-sdk';
import '@anter/ai-chat-sdk/styles-no-base.css';
import './assistant-chat.css';
import { useAuth } from '../auth/AuthContext';
import { useTheme } from '../themes/ThemeContext';
import { assistantAdapter } from './AssistantAdapter';
import { useAssistant } from './AssistantProvider';
import { AssistantSidepanel } from './AssistantSidepanel';
import {
  resolveAssistantPanelLayout,
  SIDEBAR_COLLAPSED_PX,
} from './panelSizing';

const chatTheme = {
  bg: 'var(--bg-surface)',
  sidebarBg: 'var(--bg-sidebar)',
  artifactBg: 'var(--bg-subtle)',
  border: 'var(--border-subtle)',
  accent: 'var(--brand-primary)',
  accentHover: 'var(--brand-primary)',
  accentForeground: 'var(--brand-text, #ffffff)',
  messageUserBg: 'var(--brand-primary)',
  messageUserText: 'var(--brand-text, #ffffff)',
  messageAiText: 'var(--text-main)',
  muted: 'var(--text-muted)',
  radiusSm: '8px',
  radiusMd: '12px',
  radiusLg: '16px',
};

const themeOptions = { light: chatTheme, dark: chatTheme };

function sectionTitleFromPath(pathname: string): string {
  const parts = pathname.split('/').filter(Boolean);
  const section = parts.length > 2 ? parts[2] : 'dashboard';
  const names: Record<string, string> = { items: 'Products & services' };
  return names[section] ?? section.charAt(0).toUpperCase() + section.slice(1);
}

type AssistantMountProps = {
  children: React.ReactNode;
  organizationId?: string;
  companySlug?: string;
  companyId?: string;
};

/**
 * Wraps the AccountGo main column so ChatSidepanelLayout can reflow it.
 * Sidebar stays outside this mount (persistent nav).
 */
export function AssistantMount({
  children,
  organizationId,
  companySlug,
  companyId,
}: AssistantMountProps) {
  const { isAuthenticated } = useAuth();
  const { resolvedScheme } = useTheme();
  const { isOpen, close, setCompactHost, setPageContext } = useAssistant();
  const location = useLocation();
  const { orgSlug } = useParams<{ orgSlug: string }>();
  const [viewportWidth, setViewportWidth] = useState(() =>
    typeof window === 'undefined' ? 1440 : window.innerWidth,
  );

  useEffect(() => {
    const onResize = () => setViewportWidth(window.innerWidth);
    window.addEventListener('resize', onResize);
    return () => window.removeEventListener('resize', onResize);
  }, []);

  useEffect(() => {
    setPageContext({
      pageRoute: location.pathname,
      pageTitle: sectionTitleFromPath(location.pathname),
      organizationId: organizationId || companySlug || orgSlug,
      companySlug: companySlug || orgSlug,
      companyId,
    });
  }, [
    companyId,
    companySlug,
    location.pathname,
    orgSlug,
    organizationId,
    setPageContext,
  ]);

  // While open, sidebar is forced collapsed (P0) — size against that width.
  const panelLayout = useMemo(
    () => resolveAssistantPanelLayout(viewportWidth, SIDEBAR_COLLAPSED_PX),
    [viewportWidth],
  );

  useEffect(() => {
    setCompactHost(isOpen && panelLayout.compactHost);
  }, [isOpen, panelLayout.compactHost, setCompactHost]);

  if (!isAuthenticated) {
    return <>{children}</>;
  }

  const orgKey = organizationId || companySlug || orgSlug || 'accountgo';

  return (
    <ChatProvider
      key={orgKey}
      organizationId={orgKey}
      adapter={assistantAdapter}
      config={{
        theme: resolvedScheme === 'light' ? 'light' : 'dark',
        themeOptions,
        enableModelSelector: false,
        enableSlashCommands: false,
        enableCommandPalette: false,
        enableFileUpload: false,
        enableSlashFocusShortcut: false,
        enableArtifacts: true,
      }}
      strings={{
        composerPlaceholder: 'Ask AccountGo Assistant...',
        footerDisclaimer: 'AI responses can contain mistakes. Verify before posting journals.',
      }}
    >
      <ChatSidepanelLayout
        isOpen={isOpen}
        onClose={close}
        sidepanel={<AssistantSidepanel />}
        defaultWidth={panelLayout.defaultWidth}
        minWidth={panelLayout.minWidth}
        maxWidth={panelLayout.maxWidth}
        storageKey="accountgo:assistant-sidepanel-layout"
        ariaLabel="AccountGo Assistant panel"
        className={`accountgo-assistant-layout${panelLayout.compactHost && isOpen ? ' accountgo-assistant-compact' : ''}`}
      >
        {children}
      </ChatSidepanelLayout>
    </ChatProvider>
  );
}
