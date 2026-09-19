import { computed, Injectable, signal } from '@angular/core';

export type ThemeMode = 'light' | 'dark' | 'system';

const STORAGE_KEY = 'neuraldamage.theme';

/**
 * Light / dark / system, matching the other apps in this family. "system"
 * follows the OS and keeps following it as it changes, rather than freezing
 * whatever it happened to be at load.
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly systemDark = signal(this.readSystem());
  readonly mode = signal<ThemeMode>(this.readStored());
  readonly resolved = computed(() =>
    this.mode() === 'system' ? (this.systemDark() ? 'dark' : 'light') : this.mode(),
  );

  constructor() {
    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', (event) => {
      this.systemDark.set(event.matches);
      this.apply();
    });
    this.apply();
  }

  set(mode: ThemeMode): void {
    this.mode.set(mode);
    localStorage.setItem(STORAGE_KEY, mode);
    this.apply();
  }

  private apply(): void {
    document.documentElement.classList.toggle('dark', this.resolved() === 'dark');
  }

  private readSystem(): boolean {
    return window.matchMedia('(prefers-color-scheme: dark)').matches;
  }

  private readStored(): ThemeMode {
    const stored = localStorage.getItem(STORAGE_KEY);
    return stored === 'light' || stored === 'dark' || stored === 'system' ? stored : 'system';
  }
}
