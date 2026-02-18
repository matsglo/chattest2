import { Component, Input, Output, EventEmitter, DoCheck } from '@angular/core';
import { UIMessage } from 'ai';
import { marked, Renderer, Tokens } from 'marked';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';

const renderer = new Renderer();
const defaultCodeRenderer = renderer.code.bind(renderer);

renderer.code = function (token: Tokens.Code) {
  if (token.lang === 'svg' || (!token.lang && token.text.trimStart().startsWith('<svg'))) {
    const id = 'svg-' + Math.random().toString(36).slice(2, 9);
    return `
      <div class="svg-preview my-3 rounded-lg border border-gray-200 overflow-hidden">
        <div class="flex items-center justify-between px-3 py-1.5 bg-gray-50 border-b border-gray-200 text-xs text-gray-500">
          <span>SVG Preview</span>
          <button onclick="
            var details = document.getElementById('${id}');
            details.open = !details.open;
            this.textContent = details.open ? 'Hide code' : 'View code';
          " class="hover:text-gray-700 transition-colors cursor-pointer">View code</button>
        </div>
        <div class="p-4 flex justify-center bg-white svg-container">${token.text}</div>
        <details id="${id}" class="border-t border-gray-200">
          <summary class="hidden"></summary>
          <pre class="px-3 py-2 overflow-x-auto text-xs text-gray-600 bg-gray-50 m-0"><code>${token.text.replace(/</g, '&lt;').replace(/>/g, '&gt;')}</code></pre>
        </details>
      </div>`;
  }
  return defaultCodeRenderer(token);
};

marked.setOptions({ breaks: true, gfm: true, renderer });

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
              <div class="my-2 rounded-lg border border-gray-200 bg-gray-50 text-xs">
                <!-- Tool header -->
                <div class="flex items-center gap-2 px-3 py-2">
                  @switch ($any(part).state) {
                    @case ('approval-requested') {
                      <span class="inline-block w-2.5 h-2.5 rounded-full bg-amber-400"></span>
                    }
                    @case ('input-available') {
                      <span class="inline-block w-3 h-3 border-2 border-blue-400 border-t-transparent rounded-full animate-spin"></span>
                    }
                    @case ('approval-responded') {
                      <span class="inline-block w-3 h-3 border-2 border-blue-400 border-t-transparent rounded-full animate-spin"></span>
                    }
                    @case ('output-available') {
                      <span class="inline-block w-2.5 h-2.5 rounded-full bg-green-500"></span>
                    }
                    @case ('output-denied') {
                      <span class="inline-block w-2.5 h-2.5 rounded-full bg-red-400"></span>
                    }
                    @case ('output-error') {
                      <span class="inline-block w-2.5 h-2.5 rounded-full bg-red-500"></span>
                    }
                    @default {
                      <span class="inline-block w-3 h-3 border-2 border-gray-400 border-t-transparent rounded-full animate-spin"></span>
                    }
                  }
                  <span class="font-medium text-gray-700">{{ $any(part).toolName }}</span>
                  @switch ($any(part).state) {
                    @case ('approval-requested') {
                      <span class="text-amber-600 ml-auto">Awaiting approval</span>
                    }
                    @case ('input-available') {
                      <span class="text-blue-500 ml-auto">Running...</span>
                    }
                    @case ('approval-responded') {
                      <span class="text-blue-500 ml-auto">Sending...</span>
                    }
                    @case ('output-available') {
                      <span class="text-green-600 ml-auto">Completed</span>
                    }
                    @case ('output-denied') {
                      <span class="text-red-500 ml-auto">Declined</span>
                    }
                    @case ('output-error') {
                      <span class="text-red-600 ml-auto">Error</span>
                    }
                  }
                </div>

                <!-- Collapsible arguments -->
                @if ($any(part).input) {
                  <details class="border-t border-gray-200">
                    <summary class="px-3 py-1.5 cursor-pointer select-none text-gray-500 hover:text-gray-700 transition-colors">
                      Arguments
                    </summary>
                    <pre class="px-3 py-2 overflow-x-auto text-gray-600 bg-white border-t border-gray-100">{{ formatJson($any(part).input) }}</pre>
                  </details>
                }

                <!-- Approval buttons -->
                @if ($any(part).state === 'approval-requested') {
                  <div class="flex items-center gap-2 px-3 py-2 border-t border-gray-200">
                    <button
                      (click)="toolApproval.emit({ id: $any(part).approval.id, approved: true })"
                      class="px-3 py-1 rounded-md text-xs font-medium text-white bg-green-600 hover:bg-green-700 transition-colors">
                      Allow
                    </button>
                    <button
                      (click)="toolAlwaysAllow.emit({ id: $any(part).approval.id, toolName: $any(part).toolName })"
                      class="px-3 py-1 rounded-md text-xs font-medium text-green-700 bg-green-100 hover:bg-green-200 transition-colors">
                      Always Allow
                    </button>
                    <button
                      (click)="toolApproval.emit({ id: $any(part).approval.id, approved: false })"
                      class="px-3 py-1 rounded-md text-xs font-medium text-red-700 bg-red-100 hover:bg-red-200 transition-colors">
                      Decline
                    </button>
                  </div>
                }

                <!-- Tool output -->
                @if ($any(part).state === 'output-available' && $any(part).output) {
                  <details class="border-t border-gray-200">
                    <summary class="px-3 py-1.5 cursor-pointer select-none text-gray-500 hover:text-gray-700 transition-colors">
                      Output
                    </summary>
                    <pre class="px-3 py-2 overflow-x-auto text-gray-600 bg-white border-t border-gray-100 max-h-48 overflow-y-auto">{{ formatJson($any(part).output) }}</pre>
                  </details>
                }
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
  @Output() toolApproval = new EventEmitter<{ id: string; approved: boolean }>();
  @Output() toolAlwaysAllow = new EventEmitter<{ id: string; toolName: string }>();
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

  formatJson(value: unknown): string {
    try {
      return JSON.stringify(value, null, 2);
    } catch {
      return String(value);
    }
  }
}
