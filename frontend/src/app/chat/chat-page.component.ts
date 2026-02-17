import { Component, signal } from '@angular/core';
import { Chat } from '@ai-sdk/angular';
import {
  DefaultChatTransport,
  UIMessage,
  lastAssistantMessageIsCompleteWithApprovalResponses,
} from 'ai';
import { SessionSidebarComponent, SessionSummary } from './session-sidebar.component';
import { MessageListComponent } from './message-list.component';
import { ChatInputComponent } from './chat-input.component';

@Component({
  selector: 'app-chat-page',
  standalone: true,
  imports: [SessionSidebarComponent, MessageListComponent, ChatInputComponent],
  templateUrl: './chat-page.component.html'
})
export class ChatPageComponent {
  sessions = signal<SessionSummary[]>([]);
  activeSessionId = signal<string | null>(null);
  alwaysAllowedTools = signal(new Set<string>());

  chat = signal(this.createChat('/api/chat/sessions/_none_/messages'));

  constructor() {
    this.loadSessions();
  }

  private createChat(api: string, messages?: UIMessage[]): Chat {
    return new Chat({
      transport: new DefaultChatTransport({ api }),
      messages,
      sendAutomaticallyWhen: lastAssistantMessageIsCompleteWithApprovalResponses,
      onError: (err) => console.error('Chat error:', err),
      onFinish: () => {
        this.autoApproveAlwaysAllowedTools();
        this.loadSessions();
      }
    });
  }

  private autoApproveAlwaysAllowedTools() {
    const alwaysAllowed = this.alwaysAllowedTools();
    if (alwaysAllowed.size === 0) return;

    const chat = this.chat();
    const messages = chat.messages;
    const lastMsg = messages.filter(m => m.role === 'assistant').at(-1);
    if (!lastMsg) return;

    for (const part of lastMsg.parts) {
      if (
        part.type === 'dynamic-tool' &&
        (part as any).state === 'approval-requested' &&
        alwaysAllowed.has((part as any).toolName)
      ) {
        chat.addToolApprovalResponse({
          id: (part as any).approval.id,
          approved: true,
        });
      }
    }
  }

  async loadSessions() {
    const res = await fetch('/api/chat/sessions');
    this.sessions.set(await res.json());
  }

  async createSession() {
    const res = await fetch('/api/chat/sessions', { method: 'POST' });
    const session: SessionSummary = await res.json();
    this.sessions.update(list => [session, ...list]);
    await this.selectSession(session.id);
  }

  async selectSession(id: string) {
    this.activeSessionId.set(id);
    const res = await fetch(`/api/chat/sessions/${id}/messages`);
    const messages: UIMessage[] = await res.json();
    this.chat.set(this.createChat(`/api/chat/sessions/${id}/messages`, messages));
  }

  async deleteSession(id: string) {
    await fetch(`/api/chat/sessions/${id}`, { method: 'DELETE' });
    this.sessions.update(list => list.filter(s => s.id !== id));
    if (this.activeSessionId() === id) {
      const remaining = this.sessions();
      if (remaining.length > 0) {
        this.selectSession(remaining[0].id);
      } else {
        this.activeSessionId.set(null);
        this.chat.set(this.createChat('/api/chat/sessions/_none_/messages'));
      }
    }
  }

  onSend(text: string) {
    if (!this.activeSessionId()) {
      this.createSession().then(() =>
        this.chat().sendMessage({ text })
      );
      return;
    }
    this.chat().sendMessage({ text });
  }

  onToolApproval(event: { id: string; approved: boolean }) {
    this.chat().addToolApprovalResponse(event);
  }

  onToolAlwaysAllow(event: { id: string; toolName: string }) {
    this.alwaysAllowedTools.update(set => {
      const next = new Set(set);
      next.add(event.toolName);
      return next;
    });
    this.chat().addToolApprovalResponse({ id: event.id, approved: true });
  }

  hasPendingApprovals(): boolean {
    return this.chat().messages.some(msg =>
      msg.parts.some(p =>
        p.type === 'dynamic-tool' &&
        (p as any).state === 'approval-requested'
      )
    );
  }
}
