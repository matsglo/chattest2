import { Component, signal } from '@angular/core';
import { Chat } from '@ai-sdk/angular';
import { DefaultChatTransport, UIMessage } from 'ai';
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

  chat = this.createChat('/api/chat/sessions/_none_/messages');

  constructor() {
    this.loadSessions();
  }

  private createChat(api: string, messages?: UIMessage[]): Chat {
    return new Chat({
      transport: new DefaultChatTransport({ api }),
      messages,
      onError: (err) => console.error('Chat error:', err),
      onFinish: () => this.loadSessions()
    });
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
    this.chat = this.createChat(`/api/chat/sessions/${id}/messages`, messages);
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
        this.chat = this.createChat('/api/chat/sessions/_none_/messages');
      }
    }
  }

  onSend(text: string) {
    if (!this.activeSessionId()) {
      this.createSession().then(() =>
        this.chat.sendMessage({ text })
      );
      return;
    }
    this.chat.sendMessage({ text });
  }
}
