import { useEffect, useState } from 'react';
import {
  Sparkles,
  Landmark,
  FileText,
  TrendingUp,
  CheckCircle2,
  Paperclip,
  RotateCcw,
  X,
} from 'lucide-react';
import { ChatSidepanel, useChat } from '@anter/ai-chat-sdk';
import { useAuth } from '../auth/AuthContext';
import { DocumentCapturePanel } from '../components/document-capture/DocumentCapturePanel';
import type {
  AssistantCaptureFields,
  DocumentCaptureCapabilities,
  DocumentCaptureView,
} from '../components/document-capture/fields';
import { assistantAdapter } from './AssistantAdapter';
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
export function AssistantSidepanel({ orgSlug = '' }: { orgSlug?: string }) {
  const { close } = useAssistant();
  const { authFetch } = useAuth();

  const [capabilities, setCapabilities] = useState<DocumentCaptureCapabilities | null>(null);
  const [captureOpen, setCaptureOpen] = useState(false);
  const [attachedCapture, setAttachedCapture] = useState<DocumentCaptureView | null>(null);
  const [lastAcceptedCapture, setLastAcceptedCapture] = useState<DocumentCaptureView | null>(null);

  useEffect(() => {
    setCaptureOpen(false);
    setAttachedCapture(null);
    setLastAcceptedCapture(null);
    assistantAdapter.setPendingCaptureId(null);

    let cancelled = false;
    (async () => {
      try {
        const res = await authFetch('/api/document-captures/capabilities');
        if (cancelled) return;
        if (!res.ok) {
          setCapabilities(null);
          return;
        }
        const data: DocumentCaptureCapabilities = await res.json();
        if (!cancelled) setCapabilities(data);
      } catch {
        if (!cancelled) setCapabilities(null);
      }
    })();

    return () => {
      cancelled = true;
      assistantAdapter.setPendingCaptureId(null);
    };
  }, [authFetch, orgSlug]);

  const canAttachDocument = Boolean(
    capabilities?.enabled &&
      capabilities.providerAvailable &&
      capabilities.kinds.includes('assistant-attachment')
  );

  const handleAccepted = (canonical: DocumentCaptureView) => {
    if (canonical.kind !== 'assistant-attachment' || canonical.status !== 'accepted') return;
    setAttachedCapture(canonical);
    setLastAcceptedCapture(canonical);
    assistantAdapter.setPendingCaptureId(canonical.id);
    setCaptureOpen(false);
  };

  const handleDetach = () => {
    setAttachedCapture(null);
    assistantAdapter.setPendingCaptureId(null);
  };

  const handleReuse = () => {
    if (!lastAcceptedCapture) return;
    setAttachedCapture(lastAcceptedCapture);
    assistantAdapter.setPendingCaptureId(lastAcceptedCapture.id);
  };

  const attachedFieldCount = attachedCapture?.acceptedFields
    ? ((attachedCapture.acceptedFields as AssistantCaptureFields).fields?.length ?? 0)
    : 0;

  return (
    <div className="accountgo-assistant-sidepanel flex flex-col h-full min-h-0">
      {(canAttachDocument || attachedCapture || lastAcceptedCapture) && (
        <div className="shrink-0 px-3 py-2 border-b border-[var(--border-subtle)] bg-[var(--bg-surface)] space-y-2">
          <div className="flex flex-wrap items-center justify-between gap-2">
            {canAttachDocument && (
              <button
                type="button"
                onClick={() => setCaptureOpen((open) => !open)}
                className="inline-flex items-center gap-1.5 px-2.5 py-1.5 text-xs font-semibold rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-subtle)] text-[var(--text-main)] hover:bg-[var(--bg-surface-hover)] transition"
              >
                <Paperclip className="w-3.5 h-3.5 text-[var(--brand-primary)]" />
                {captureOpen ? 'Hide document attachment' : 'Attach document'}
              </button>
            )}

            {attachedCapture ? (
              <div
                role="status"
                className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-lg bg-[var(--brand-subtle)] text-[var(--brand-primary)] text-xs font-medium"
              >
                <span>
                  Attached {attachedCapture.id} ({attachedFieldCount} masked{' '}
                  {attachedFieldCount === 1 ? 'field' : 'fields'})
                </span>
                <button
                  type="button"
                  onClick={handleDetach}
                  aria-label="Remove attachment"
                  className="p-0.5 rounded hover:bg-black/10 transition"
                >
                  <X className="w-3 h-3" />
                </button>
              </div>
            ) : (
              lastAcceptedCapture && (
                <button
                  type="button"
                  onClick={handleReuse}
                  className="inline-flex items-center gap-1 px-2.5 py-1 text-xs font-medium rounded-lg border border-[var(--border-subtle)] bg-[var(--bg-subtle)] text-[var(--text-secondary)] hover:text-[var(--text-main)] transition"
                >
                  <RotateCcw className="w-3 h-3" />
                  Reuse {lastAcceptedCapture.id}
                </button>
              )
            )}
          </div>

          {captureOpen && canAttachDocument && (
            <div className="max-h-96 overflow-y-auto">
              <DocumentCapturePanel
                kind="assistant-attachment"
                orgSlug={orgSlug}
                scopeKey="assistant"
                capabilities={capabilities}
                onAccepted={handleAccepted}
              />
            </div>
          )}
        </div>
      )}

      <div className="flex-1 min-h-0">
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
    </div>
  );
}
