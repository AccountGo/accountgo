import type {
  ChatAdapter,
  ListParams,
  MessagePayload,
  SendMessageOptions,
  SessionPatch,
  SessionWithMessages,
  ResumeState,
} from '@anter/ai-chat-sdk';
import { assistantFetch, assistantFetchJson, assistantFetchStream } from './assistantFetch';
import {
  AssistantPageContext,
  buildRunStreamBody,
  mergeAssistantContextVariables,
} from './contextVariables';

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

  setPageContext(context: AssistantPageContext): void {
    this.pageContext = { ...context };
  }

  async createSession(): Promise<string> {
    return generateSessionId();
  }

  async loadSession(sessionId: string): Promise<SessionWithMessages> {
    const session = await assistantFetchJson<UpstreamSession>(
      `/api/ai/chat/sessions/${encodeURIComponent(sessionId)}`,
    );
    const turns = Array.isArray(session?.turns) ? session.turns : [];
    const messages = turns.map((turn, idx) => ({
      id: `${sessionId}_${turn?.seq ?? idx}`,
      role: (turn?.role === 'assistant' ? 'assistant' : 'user') as 'user' | 'assistant',
      content: typeof turn?.content === 'string' ? turn.content : '',
      timestamp: toDate(turn?.fullTimestamp || turn?.ts),
    }));

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
    const body = buildRunStreamBody(payload.message, sessionId, contextVariables);

    return assistantFetchStream('/api/ai/chat/run-stream', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
      signal: options?.signal,
    });
  }

  async getExecutionStream(
    executionId: string,
    resumeFrom = 0,
    options?: SendMessageOptions,
  ): Promise<ReadableStream<Uint8Array>> {
    const from = Number.isFinite(Number(resumeFrom)) ? Number(resumeFrom) : 0;
    return assistantFetchStream(
      `/api/ai/chat/executions/${encodeURIComponent(executionId)}/stream?resumeFrom=${encodeURIComponent(from)}`,
      { method: 'GET', signal: options?.signal },
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
    return assistantFetchStream(
      `/api/ai/chat/executions/${encodeURIComponent(executionId)}/resume`,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: '{}',
        signal: options?.signal,
      },
    );
  }

  async resolveToolApproval({
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
