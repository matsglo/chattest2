import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [RouterLink],
  template: `
    <div class="min-h-screen bg-gray-50 dark:bg-gray-900 text-gray-900 dark:text-gray-100">
      <div class="max-w-xl mx-auto px-4 py-8">
        <div class="flex items-center gap-3 mb-8">
          <a routerLink="/chat"
             class="text-gray-500 hover:text-gray-700 dark:text-gray-400 dark:hover:text-gray-200 transition-colors">
            ← Back
          </a>
          <h1 class="text-xl font-semibold">Settings</h1>
        </div>

        <div class="bg-white dark:bg-gray-800 rounded-xl border border-gray-200 dark:border-gray-700 divide-y divide-gray-200 dark:divide-gray-700">
          <div class="flex items-center justify-between px-4 py-3">
            <div>
              <div class="text-sm font-medium">Show token usage</div>
              <div class="text-xs text-gray-500 dark:text-gray-400">Display input, output, and cached token counts per message and per session</div>
            </div>
            <button
              (click)="toggleTokenUsage()"
              [class]="showTokenUsage
                ? 'relative w-10 h-6 rounded-full bg-blue-600 transition-colors'
                : 'relative w-10 h-6 rounded-full bg-gray-300 dark:bg-gray-600 transition-colors'">
              <span
                [class]="showTokenUsage
                  ? 'absolute top-0.5 left-4.5 w-5 h-5 bg-white rounded-full shadow transition-all'
                  : 'absolute top-0.5 left-0.5 w-5 h-5 bg-white rounded-full shadow transition-all'">
              </span>
            </button>
          </div>
        </div>
      </div>
    </div>
  `
})
export class SettingsComponent {
  showTokenUsage = localStorage.getItem('show-token-usage') === 'true';

  toggleTokenUsage() {
    this.showTokenUsage = !this.showTokenUsage;
    localStorage.setItem('show-token-usage', String(this.showTokenUsage));
  }
}
