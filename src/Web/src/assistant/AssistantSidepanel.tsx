import { Sparkles, Landmark, FileText, TrendingUp, CheckCircle2 } from 'lucide-react';
import { ChatSidepanel, useChat } from '@anter/ai-chat-sdk';
import { useAssistant } from './AssistantProvider';

const STARTER_PROMPTS = [
  {
    icon: Landmark,
    label: 'What is our current cash position across all bank accounts?',
  },
  {
    icon: FileText,
    label: 'Which customer invoices are overdue and need follow-up?',
  },
  {
    icon: TrendingUp,
    label: 'Summarize our revenue and top expenses for this month',
  },
  {
    icon: CheckCircle2,
    label: 'Are there any unreconciled transactions or open bills to review?',
  },
];

function AssistantEmptyState() {
  const { sendMessage } = useChat();

  return (
    <div style={{ textAlign: 'center', opacity: 0.95, padding: '0 1.25rem' }}>
      <Sparkles className="mx-auto mb-3 text-[var(--brand-primary)]" size={32} />
      <p style={{ fontWeight: 600, marginBottom: 4, fontSize: '1rem' }}>
        Ask AccountGo Assistant
      </p>
      <p
        className="px-4"
        style={{
          fontSize: '0.85em',
          opacity: 0.8,
          paddingLeft: '1rem',
          paddingRight: '1rem',
          margin: '0 auto 1.25rem',
          maxWidth: '28rem',
        }}
      >
        Get help with double-entry bookkeeping, journals, invoices, and reports for this
        workspace.
      </p>

      <div
        className="accountgo-starter-prompts-grid"
        style={{
          display: 'flex',
          flexDirection: 'column',
          gap: '8px',
          maxWidth: '28rem',
          margin: '0 auto',
          textAlign: 'left',
        }}
      >
        {STARTER_PROMPTS.map((item) => {
          const Icon = item.icon;
          return (
            <button
              key={item.label}
              type="button"
              onClick={() => void sendMessage(item.label)}
              className="accountgo-starter-prompt-btn"
            >
              <span className="accountgo-starter-prompt-icon">
                <Icon size={14} />
              </span>
              <span className="accountgo-starter-prompt-text">{item.label}</span>
            </button>
          );
        })}
      </div>
    </div>
  );
}

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
        enableTools={false}
        enableVoiceInput={false}
        brandIcon={
          <span aria-hidden="true" className="ais-sidepanel-brand-badge">
            <Sparkles size={13} />
          </span>
        }
        emptyState={<AssistantEmptyState />}
      />
    </div>
  );
}
