import { Component, Input, Output, EventEmitter, ElementRef, ViewChild, AfterViewChecked } from '@angular/core';
import { UIMessage } from 'ai';
import { MessageBubbleComponent } from './message-bubble.component';

@Component({
  selector: 'app-message-list',
  standalone: true,
  imports: [MessageBubbleComponent],
  template: `
    <div #scrollContainer class="h-full overflow-y-auto">
      <div class="max-w-3xl mx-auto px-4 py-6">
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
            <div class="bg-white border border-gray-200 rounded-2xl rounded-bl-md px-4 py-2 shadow-sm">
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
            <div class="bg-red-50 border border-red-200 text-red-700 rounded-xl px-4 py-2 text-sm max-w-[80%]">
              {{ error.message || 'An error occurred. Please try again.' }}
            </div>
          </div>
        }
        @if (messages.length === 0 && !error) {
          <div class="text-center text-gray-400 mt-32 space-y-2">
            <div class="text-4xl">💬</div>
            <div>Send a message to start the conversation.</div>
          </div>
        }
      </div>
    </div>
  `
})
export class MessageListComponent implements AfterViewChecked {
  @Input() messages: UIMessage[] = [];
  @Input() status: string = 'ready';
  @Input() error: Error | undefined;
  @Output() toolApproval = new EventEmitter<{ id: string; approved: boolean }>();
  @Output() toolAlwaysAllow = new EventEmitter<{ id: string; toolName: string }>();
  @ViewChild('scrollContainer') private scrollContainer!: ElementRef;

  private shouldScroll = true;

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
