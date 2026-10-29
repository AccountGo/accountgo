import { Sparkles } from 'lucide-react';
import { ChatSidepanel } from '@anter/ai-chat-sdk';
import { useAssistant } from './AssistantProvider';

/**
 * Chat panel content for AccountGo Assistant.
 */
export function AssistantSidepanel() {
  const { close } = useAssistant();

  return (
    <div className="accountgo-assistant-sidepanel">
      <ChatSidepanel
        title="AccountGo Assistant"
        subtitle="Your accounting AI sidekick"
        onClose={close}
        brandIcon={
          <span aria-hidden="true" className="ais-sidepanel-brand-badge">
            <Sparkles size={13} />
          </span>
        }
        emptyState={
          <div style={{ textAlign: 'center', opacity: 0.8 }}>
            <Sparkles className="mx-auto mb-3 text-[var(--brand-primary)]" size={32} />
            <p style={{ fontWeight: 600, marginBottom: 4, fontSize: '1rem' }}>
              Ask AccountGo Assistant
            </p>
            <p style={{ fontSize: '0.85em', opacity: 0.8 }}>
              Get help with double-entry bookkeeping, journals, invoices, and reports for this
              workspace.
            </p>
          </div>
        }
      />
    </div>
  );
}
