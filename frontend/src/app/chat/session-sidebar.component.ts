import { Component, Input, Output, EventEmitter } from '@angular/core';
import { RouterLink } from '@angular/router';

export interface SessionSummary {
  id: string;
  title: string;
}

@Component({
  selector: 'app-session-sidebar',
  standalone: true,
  imports: [RouterLink],
  template: `
    <aside class="w-64 bg-gray-900 text-white flex flex-col h-full">
      <div class="p-4 flex gap-2">
        <button
          (click)="newSession.emit()"
          class="flex-1 rounded-lg border border-gray-600 px-3 py-2 text-sm
                 hover:bg-gray-800 transition">
          + New Chat
        </button>
        <button
          (click)="toggleDarkMode()"
          class="rounded-lg border border-gray-600 px-2.5 py-2 text-sm
                 hover:bg-gray-800 transition"
          [title]="isDark ? 'Switch to light mode' : 'Switch to dark mode'">
          {{ isDark ? '☀️' : '🌙' }}
        </button>
        <a
          routerLink="/settings"
          class="rounded-lg border border-gray-600 px-2.5 py-2 text-sm
                 hover:bg-gray-800 transition"
          title="Settings">
          ⚙
        </a>
      </div>
      <nav class="flex-1 overflow-y-auto px-2 space-y-1">
        @for (s of sessions; track s.id) {
          <div class="group flex items-center">
            <button
              (click)="sessionSelected.emit(s.id)"
              [class]="s.id === activeId
                ? 'flex-1 text-left rounded-lg bg-gray-700 px-3 py-2 text-sm truncate'
                : 'flex-1 text-left rounded-lg px-3 py-2 text-sm truncate hover:bg-gray-800'">
              {{ s.title }}
            </button>
            <button
              (click)="sessionDeleted.emit(s.id); $event.stopPropagation()"
              class="opacity-0 group-hover:opacity-100 px-2 py-1 text-gray-400
                     hover:text-red-400 text-xs transition-opacity">
              ✕
            </button>
          </div>
        }
      </nav>
    </aside>
  `
})
export class SessionSidebarComponent {
  @Input() sessions: SessionSummary[] = [];
  @Input() activeId: string | null = null;
  @Output() sessionSelected = new EventEmitter<string>();
  @Output() newSession = new EventEmitter<void>();
  @Output() sessionDeleted = new EventEmitter<string>();

  isDark = document.documentElement.classList.contains('dark');

  toggleDarkMode() {
    this.isDark = !this.isDark;
    document.documentElement.classList.toggle('dark', this.isDark);
    localStorage.setItem('dark-mode', String(this.isDark));
  }
}
