import { Component, Input, Output, EventEmitter, ElementRef, ViewChild, AfterViewChecked, DoCheck } from '@angular/core';
import { UIMessage } from 'ai';
import { MessageBubbleComponent } from './message-bubble.component';

@Component({
  selector: 'app-message-list',
  standalone: true,
  imports: [MessageBubbleComponent],
  template: `
    <div #scrollContainer class="h-full overflow-y-auto">
      <div class="max-w-3xl mx-auto px-4 py-6">
        @if (showUsage && messages.length > 0 && sessionTotals.totalTokens > 0) {
          <div class="text-[11px] text-gray-400 dark:text-gray-500 text-center py-1 border-b border-gray-200 dark:border-gray-700 mb-2">
            Session: ↑{{ sessionTotals.inputTokens }} input · ↓{{ sessionTotals.outputTokens }} output
            @if (sessionTotals.cachedTokens) {
               · ⚡{{ sessionTotals.cachedTokens }} cached
            }
          </div>
        }
        @for (msg of messages; track msg.id; let last = $last) {
          <div class="mt-4">
            <app-message-bubble
              [message]="msg"
              [isStreaming]="last && status === 'streaming'"
              (toolApproval)="toolApproval.emit($event)"
              (toolAlwaysAllow)="toolAlwaysAllow.emit($event)"
            />
          </div>
        }
        @if (status === 'submitted') {
          <div class="flex justify-start">
            <div class="bg-white dark:bg-gray-800 border border-gray-200 dark:border-gray-700 rounded-2xl rounded-bl-md px-4 py-2 shadow-sm">
              <div class="flex items-center gap-1.5 text-sm text-gray-400">
                <span class="flex gap-1">
                  <span class="w-1.5 h-1.5 bg-gray-400 rounded-full animate-bounce [animation-delay:-0.3s]"></span>
                  <span class="w-1.5 h-1.5 bg-gray-400 rounded-full animate-bounce [animation-delay:-0.15s]"></span>
                  <span class="w-1.5 h-1.5 bg-gray-400 rounded-full animate-bounce"></span>
                </span>
              </div>
            </div>
          </div>
        }
        @if (error) {
          <div class="flex justify-center">
            <div class="bg-red-50 dark:bg-red-900/30 border border-red-200 dark:border-red-800 text-red-700 dark:text-red-400 rounded-xl px-4 py-2 text-sm max-w-[80%]">
              {{ error.message || 'An error occurred. Please try again.' }}
            </div>
          </div>
        }
        @if (messages.length === 0 && !error) {
          <div class="text-center text-gray-400 dark:text-gray-500 mt-32 space-y-4">
            <div class="text-4xl">💬</div>
            <div>Send a message to start the conversation.</div>
            <div class="flex flex-wrap justify-center gap-2 mt-4">
              @for (s of suggestions; track s) {
                <button
                  (click)="suggestionClicked.emit(s)"
                  class="px-3 py-1.5 text-sm rounded-full border border-gray-300 dark:border-gray-600
                         text-gray-600 dark:text-gray-300 bg-white dark:bg-gray-800
                         hover:bg-gray-100 dark:hover:bg-gray-700 transition-colors">
                  {{ s }}
                </button>
              }
            </div>
          </div>
        }
      </div>
    </div>
  `
})
export class MessageListComponent implements AfterViewChecked, DoCheck {
  @Input() messages: UIMessage[] = [];
  @Input() status: string = 'ready';
  @Input() error: Error | undefined;
  @Output() toolApproval = new EventEmitter<{ id: string; approved: boolean }>();
  @Output() toolAlwaysAllow = new EventEmitter<{ id: string; toolName: string }>();
  @Output() suggestionClicked = new EventEmitter<string>();

  showUsage = localStorage.getItem('show-token-usage') === 'true';
  sessionTotals = { inputTokens: 0, outputTokens: 0, cachedTokens: 0, totalTokens: 0 };

  suggestions = [
    'What time is it in Norway?',
    'Create a funny face in SVG',
    'I need a painting',
  ];
  @ViewChild('scrollContainer') private scrollContainer!: ElementRef;

  private shouldScroll = true;

  ngDoCheck() {
    this.showUsage = localStorage.getItem('show-token-usage') === 'true';
    if (this.showUsage) {
      const totals = { inputTokens: 0, outputTokens: 0, cachedTokens: 0, totalTokens: 0 };
      for (const msg of this.messages) {
        const usage = (msg as any).metadata?.usage;
        if (usage) {
          totals.inputTokens += usage.inputTokens ?? 0;
          totals.outputTokens += usage.outputTokens ?? 0;
          totals.cachedTokens += usage.cachedTokens ?? 0;
          totals.totalTokens += usage.totalTokens ?? 0;
        }
      }
      this.sessionTotals = totals;
    }
  }

  ngAfterViewChecked() {
    if (this.shouldScroll) {
      this.scrollToBottom();
    }
  }

  private scrollToBottom() {
    const el = this.scrollContainer?.nativeElement;
    if (el) {
      el.scrollTop = el.scrollHeight;
    }
  }
}
