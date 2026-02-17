import { Component, Input, DoCheck } from '@angular/core';
import { UIMessage } from 'ai';
import { marked } from 'marked';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';

marked.setOptions({ breaks: true, gfm: true });

@Component({
  selector: 'app-message-bubble',
  standalone: true,
  template: `
    <div [class]="message.role === 'user'
      ? 'flex justify-end'
      : 'flex justify-start'">
      <div [class]="message.role === 'user'
        ? 'bg-blue-600 text-white rounded-2xl rounded-br-md px-4 py-2 max-w-[80%]'
        : 'bg-white border border-gray-200 rounded-2xl rounded-bl-md px-4 py-2 max-w-[80%] shadow-sm'">
        @if (message.role === 'user') {
          <div class="whitespace-pre-wrap text-sm">{{ textContent }}</div>
        } @else {
          @if (reasoningText) {
            <details class="mb-2 text-xs text-gray-400">
              <summary class="cursor-pointer select-none hover:text-gray-600 transition-colors">
                Thinking...
              </summary>
              <div class="mt-1 pl-3 border-l-2 border-gray-200 text-gray-500 whitespace-pre-wrap">{{ reasoningText }}</div>
            </details>
          }
          <div class="prose prose-sm max-w-none" [innerHTML]="renderedHtml"></div>
          @for (part of message.parts; track $index) {
            @if (part.type === 'dynamic-tool') {
              <div class="flex items-center gap-1.5 text-xs text-gray-400 italic my-1">
                <span class="inline-block w-3 h-3 border-2 border-gray-400 border-t-transparent rounded-full animate-spin"></span>
                {{ $any(part).toolName }}
              </div>
            }
          }
        }
      </div>
    </div>
  `
})
export class MessageBubbleComponent implements DoCheck {
  @Input({ required: true }) message!: UIMessage;
  renderedHtml: SafeHtml = '';
  textContent = '';
  reasoningText = '';

  private lastText = '';
  private lastReasoning = '';

  constructor(private sanitizer: DomSanitizer) {}

  ngDoCheck() {
    const text = this.message.parts
      .filter((p): p is Extract<typeof p, { type: 'text' }> => p.type === 'text')
      .map(p => p.text)
      .join('');

    const reasoning = this.message.parts
      .filter((p): p is Extract<typeof p, { type: 'reasoning' }> => p.type === 'reasoning')
      .map(p => p.text)
      .join('');

    const changed = text !== this.lastText || reasoning !== this.lastReasoning;
    if (!changed) return;

    this.lastText = text;
    this.lastReasoning = reasoning;
    this.reasoningText = reasoning;

    if (this.message.role === 'user') {
      this.textContent = text;
    } else {
      const html = marked.parse(text) as string;
      this.renderedHtml = this.sanitizer.bypassSecurityTrustHtml(html);
    }
  }
}
