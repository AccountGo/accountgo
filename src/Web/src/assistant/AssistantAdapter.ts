import type {
  ChatAdapter,
  ChatSessionFileRef,
  ListParams,
  MessagePayload,
  SendMessageOptions,
  SessionPatch,
  SessionWithMessages,
  ResumeState,
} from '@anter/ai-chat-sdk';
import type {
  AssistantCaptureFields,
  DocumentCaptureCapabilities,
} from '../components/document-capture/fields';
import {
  rejectCapture,
  uploadAndWaitForCapture,
} from '../components/document-capture/captureClient';
import { assistantFetch, assistantFetchJson, assistantFetchStream } from './assistantFetch';
import {
  AssistantPageContext,
  buildRunStreamBody,
  mergeAssistantContextVariables,
} from './contextVariables';
import {
  createReidentifyingSseStream,
  reidentifyMessageContents,
} from './reidentify';

export type { AssistantPageContext };

function generateSessionId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return `session_${crypto.randomUUID()}`;
  }
  return `session_${Date.now()}_${Math.random().toString(36).slice(2, 10)}`;
}

function toDate(value: unknown): Date {
  const ts = typeof value === 'number' ? value : Date.parse(String(value || ''));
  if (Number.isFinite(ts)) return new Date(ts);
  return new Date();
}

function toIsoString(value: unknown): string {
  return toDate(value).toISOString();
}

function asResumeState(value: unknown): ResumeState | undefined {
  if (value === 'live' || value === 'resumable' || value === 'retry' || value === null) {
    return value;
  }
  return undefined;
}

type SessionTurn = {
  seq?: number;
  role?: string;
  content?: string;
  fullTimestamp?: string | number;
  ts?: string | number;
};

type UpstreamSession = {
  sessionId?: string;
  title?: string;
  updatedAt?: string | number;
  turns?: SessionTurn[];
  activeExecutionId?: string | null;
  resumeState?: string | null;
  resumableExecutionId?: string | null;
};

/**
 * ChatAdapter for AccountGo Assistant. Every call goes through AccountGo.Api
 * (`/api/ai/chat/*`); the browser never sees Anter credentials.
 */
export class AssistantAdapter implements ChatAdapter {
  private pageContext: AssistantPageContext = {};
  private captureCapabilities: DocumentCaptureCapabilities | null = null;
  private readonly executionSessions = new Map<string, string>();

  private async lookupPseudonymNames(
    sessionId: string,
    tokens: string[],
  ): Promise<Record<string, string>> {
    const res = await assistantFetchJson<{ names?: Record<string, string> }>(
      `/api/ai/chat/sessions/${encodeURIComponent(sessionId)}/reidentify`,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ tokens }),
      },
    );
    return res?.names && typeof res.names === 'object' ? res.names : {};
  }

  private rememberExecutionSession(executionId: string | null | undefined, sessionId: string | undefined): void {
    if (executionId && sessionId) {
      this.executionSessions.set(executionId, sessionId);
    }
  }

  setPageContext(context: AssistantPageContext): void {
    this.pageContext = { ...context };
  }

  setCaptureCapabilities(capabilities: DocumentCaptureCapabilities | null): void {
    this.captureCapabilities = capabilities;
  }

  async uploadFile(_sessionId: string, file: File): Promise<ChatSessionFileRef> {
    const view = await uploadAndWaitForCapture(
      assistantFetch,
      'assistant-attachment',
      file,
      this.captureCapabilities,
    );

    const fieldsObj = (view.acceptedFields ?? view.fields) as AssistantCaptureFields | null;
    const fieldCount = Array.isArray(fieldsObj?.fields) ? fieldsObj.fields.length : 0;
    const fieldLabel = fieldCount === 1 ? '1 field' : `${fieldCount} fields`;

    // Workaround for #98 G2: the SDK chip has no click/preview callback, so surface the extracted field count in fileName.
    return {
      id: view.id,
      fileName: `${file.name} · ${fieldLabel}`,
      mimeType: file.type || view.contentType || 'application/octet-stream',
      size: file.size,
      status: view.status === 'ready' || view.status === 'accepted' ? 'processed' : 'error',
      downloadUrl: '',
      createdAt: view.createdAt,
    };
  }

  async deleteSessionFile(_sessionId: string, fileId: string): Promise<void> {
    await rejectCapture(assistantFetch, fileId);
  }

  async createSession(): Promise<string> {
    return generateSessionId();
  }

  async loadSession(sessionId: string): Promise<SessionWithMessages> {
    const session = await assistantFetchJson<UpstreamSession>(
      `/api/ai/chat/sessions/${encodeURIComponent(sessionId)}`,
    );
    const turns = Array.isArray(session?.turns) ? session.turns : [];
    const rawMessages = turns.map((turn, idx) => ({
      id: `${sessionId}_${turn?.seq ?? idx}`,
      role: (turn?.role === 'assistant' ? 'assistant' : 'user') as 'user' | 'assistant',
      content: typeof turn?.content === 'string' ? turn.content : '',
      timestamp: toDate(turn?.fullTimestamp || turn?.ts),
    }));

    this.rememberExecutionSession(session?.activeExecutionId, sessionId);
    this.rememberExecutionSession(session?.resumableExecutionId, sessionId);

    const messages = await reidentifyMessageContents(
      sessionId,
      rawMessages,
      (sid, tokens) => this.lookupPseudonymNames(sid, tokens),
    );

    const firstUserMessage = turns.find(
      (turn) => turn?.role === 'user' && typeof turn?.content === 'string',
    );
    const titleSource = firstUserMessage?.content || 'New conversation';
    const title = titleSource.length > 50 ? `${titleSource.slice(0, 47)}...` : titleSource;

    return {
      sessionId,
      title,
      updatedAt: toIsoString(session?.updatedAt),
      status: 'active',
      messages,
      activeExecutionId: session?.activeExecutionId || undefined,
      resumeState: asResumeState(session?.resumeState),
      resumableExecutionId: session?.resumableExecutionId || undefined,
    };
  }

  async listSessions(params: ListParams = {}) {
    const items = await assistantFetchJson<UpstreamSession[] | { sessions?: UpstreamSession[] }>(
      '/api/ai/chat/sessions',
    );
    const all = Array.isArray(items) ? items : Array.isArray(items?.sessions) ? items.sessions : [];

    const normalized = all.map((session) => ({
      sessionId: String(session?.sessionId || ''),
      title:
        typeof session?.title === 'string' && session.title.trim()
          ? session.title
          : 'New conversation',
      updatedAt: toIsoString(session?.updatedAt),
      status: 'active' as const,
    }));

    const page = Math.max(1, Number(params.page || 1));
    const limit = Math.max(1, Number(params.limit || 50));
    const start = (page - 1) * limit;

    return {
      sessions: normalized.slice(start, start + limit),
      total: normalized.length,
      page,
    };
  }

  async updateSession(_sessionId: string, _patch: SessionPatch): Promise<void> {
    // Title is derived from the first user message upstream.
  }

  async deleteSession(sessionId: string): Promise<void> {
    await assistantFetch(`/api/ai/chat/sessions/${encodeURIComponent(sessionId)}`, {
      method: 'DELETE',
    });
  }

  async sendMessage(
    payload: MessagePayload,
    options?: SendMessageOptions,
  ): Promise<ReadableStream<Uint8Array>> {
    const sessionId =
      payload.sessionId ||
      (typeof payload.contextVariables?.sessionId === 'string'
        ? payload.contextVariables.sessionId
        : undefined);

    const contextVariables = mergeAssistantContextVariables(
      this.pageContext,
      payload.contextVariables,
    );

    // Tenant ids are applied server-side from ITenantContext — do not send them here.
    const body = buildRunStreamBody(
      payload.message,
      sessionId,
      contextVariables,
      payload.attachedFileIds,
    );

    const stream = await assistantFetchStream('/api/ai/chat/run-stream', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
      signal: options?.signal,
    });

    return createReidentifyingSseStream(
      stream,
      sessionId,
      (sid, tokens) => this.lookupPseudonymNames(sid, tokens),
      {
        onExecutionStarted: (executionId) => this.rememberExecutionSession(executionId, sessionId),
      },
    );
  }

  async getExecutionStream(
    executionId: string,
    resumeFrom = 0,
    options?: SendMessageOptions,
  ): Promise<ReadableStream<Uint8Array>> {
    const from = Number.isFinite(Number(resumeFrom)) ? Number(resumeFrom) : 0;
    const stream = await assistantFetchStream(
      `/api/ai/chat/executions/${encodeURIComponent(executionId)}/stream?resumeFrom=${encodeURIComponent(from)}`,
      { method: 'GET', signal: options?.signal },
    );
    return createReidentifyingSseStream(
      stream,
      this.executionSessions.get(executionId),
      (sid, tokens) => this.lookupPseudonymNames(sid, tokens),
    );
  }

  async cancelRun({ executionId }: { sessionId: string; executionId: string }): Promise<void> {
    if (!executionId) return;
    await assistantFetch(`/api/ai/chat/executions/${encodeURIComponent(executionId)}/cancel`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: '{}',
    });
  }

  async resumeExecution(
    executionId: string,
    options?: SendMessageOptions,
  ): Promise<ReadableStream<Uint8Array>> {
    const sessionId = this.executionSessions.get(executionId);
    const stream = await assistantFetchStream(
      `/api/ai/chat/executions/${encodeURIComponent(executionId)}/resume`,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(sessionId ? { sessionId } : {}),
        signal: options?.signal,
      },
    );
    return createReidentifyingSseStream(
      stream,
      sessionId,
      (sid, tokens) => this.lookupPseudonymNames(sid, tokens),
    );
  }

  async resolveToolApproval({
    sessionId,
    approval,
    decision,
    reason,
  }: {
    sessionId: string;
    approval: { executionId?: string; toolCallId?: string };
    decision: 'approved' | 'denied';
    reason?: string;
  }): Promise<void> {
    const executionId = approval?.executionId;
    const toolCallId = approval?.toolCallId;
    if (!executionId || !toolCallId) {
      throw new Error('AccountGo Assistant: approval is missing execution or tool call id');
    }
    this.rememberExecutionSession(executionId, sessionId);
    await assistantFetchJson(
      `/api/ai/chat/approvals/${encodeURIComponent(executionId)}/${encodeURIComponent(toolCallId)}`,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(reason ? { decision, reason } : { decision }),
      },
    );
  }
}

export const assistantAdapter = new AssistantAdapter();
