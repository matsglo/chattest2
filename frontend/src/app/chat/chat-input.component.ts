import { Component, Output, EventEmitter, Input, ElementRef, ViewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';

@Component({
  selector: 'app-chat-input',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="border-t bg-white px-4 py-3">
      <div class="max-w-3xl mx-auto flex gap-2 items-end">
        <textarea
          #textarea
          [(ngModel)]="text"
          (ngModelChange)="autoResize()"
          (keydown)="onKeydown($event)"
          [disabled]="disabled"
          placeholder="Type a message... (Shift+Enter for new line)"
          rows="1"
          class="flex-1 resize-none rounded-xl border border-gray-300 px-4 py-2
                 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500
                 disabled:opacity-50 max-h-40 overflow-y-auto"
        ></textarea>
        <button
          (click)="submit()"
          [disabled]="disabled || !text.trim()"
          class="rounded-xl bg-blue-600 px-4 py-2 text-sm font-medium text-white
                 hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed
                 transition-colors">
          Send
        </button>
      </div>
    </div>
  `
})
export class ChatInputComponent {
  @Input() disabled = false;
  @Output() send = new EventEmitter<string>();
  @ViewChild('textarea') private textarea!: ElementRef<HTMLTextAreaElement>;

  text = '';

  onKeydown(event: KeyboardEvent) {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      this.submit();
    }
  }

  submit() {
    const trimmed = this.text.trim();
    if (!trimmed) return;
    this.send.emit(trimmed);
    this.text = '';
    setTimeout(() => this.autoResize());
  }

  autoResize() {
    const el = this.textarea?.nativeElement;
    if (el) {
      el.style.height = 'auto';
      el.style.height = el.scrollHeight + 'px';
    }
  }
}
