import { computed, effect, Service, signal } from '@angular/core';

export type ThemeMode = 'light' | 'dark' | 'system';

const STORAGE_KEY = 'neuraldamage.theme';
const DARK_QUERY = '(prefers-color-scheme: dark)';

/** The colour scheme: a stored choice, or the operating system's when the choice is `system`. */
@Service()
export class Theme {
  private readonly systemDark = signal(matchMedia(DARK_QUERY).matches);

  readonly mode = signal<ThemeMode>(readStored());
  readonly resolved = computed(() =>
    this.mode() === 'system' ? (this.systemDark() ? 'dark' : 'light') : this.mode(),
  );

  constructor() {
    matchMedia(DARK_QUERY).addEventListener('change', (event) =>
      this.systemDark.set(event.matches),
    );
    effect(() => document.documentElement.classList.toggle('dark', this.resolved() === 'dark'));
  }

  set(mode: ThemeMode): void {
    this.mode.set(mode);
    try {
      localStorage.setItem(STORAGE_KEY, mode);
    } catch {
      // Storage blocked: the choice holds for this tab.
    }
  }
}

function readStored(): ThemeMode {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (stored === 'light' || stored === 'dark' || stored === 'system') return stored;
  } catch {
    // Storage blocked.
  }
  return 'system';
}
