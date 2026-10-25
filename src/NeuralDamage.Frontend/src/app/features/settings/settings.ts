import { Component, effect, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideGithub, lucideLogOut, lucideSticker } from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmInput } from '@spartan-ng/helm/input';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { HlmToggleGroupImports } from '@spartan-ng/helm/toggle-group';
import { AuthService } from '../../core/auth/auth.service';
import { describeApiError } from '../../core/http-errors';
import { Theme, ThemeMode } from '../../core/theme';

/**
 * `/settings`: the account as the identity provider reports it, the theme, and on phones, where the
 * rail's account menu is missing, the way to stickers, GitHub and logging out.
 */
@Component({
  selector: 'app-settings',
  imports: [RouterLink, NgIcon, HlmButton, HlmCardImports, HlmFieldImports, HlmInput, HlmSpinner, HlmToggleGroupImports],
  providers: [provideIcons({ lucideGithub, lucideLogOut, lucideSticker })],
  host: { class: 'flex h-full min-h-0 flex-col' },
  template: `
    <header class="flex h-14 shrink-0 items-center gap-2 px-4">
      <h1 class="font-display text-2xl font-extrabold tracking-tight">Settings</h1>
    </header>
    <div class="min-h-0 flex-1 overflow-y-auto">
      <div class="mx-auto flex w-full max-w-2xl flex-col gap-4 p-4">
        <section hlmCard>
          <div hlmCardHeader>
            <h2 hlmCardTitle>Account</h2>
            <p hlmCardDescription>Managed by your sign-in provider.</p>
          </div>
          <div hlmCardContent class="grid gap-2 text-sm">
            <div><span class="text-muted-foreground">Name:</span> {{ auth.displayName() }}</div>
            <div><span class="text-muted-foreground">Email:</span> {{ auth.user()?.email }}</div>
          </div>
        </section>
        <section hlmCard>
          <div hlmCardHeader>
            <h2 hlmCardTitle>Nicknames</h2>
            <p hlmCardDescription>
              Other names you go by. Bots and people can &#64;mention you by them too, and bots in your chats are told them,
              so a bot that knows you as Niclas reaches you with &#64;Niclas.
            </p>
          </div>
          <form hlmCardContent class="flex flex-col gap-3 sm:flex-row sm:items-end" (submit)="$event.preventDefault(); saveNicknames()">
            <div hlmField class="flex-1">
              <label hlmFieldLabel for="nicknames">Nicknames</label>
              <input hlmInput id="nicknames" placeholder="e.g. Niclas, Nic" [value]="nicknames()" (input)="nicknames.set($any($event.target).value)" />
              <p hlmFieldDescription>Separate them with commas, up to 5.</p>
            </div>
            <button hlmBtn type="submit" class="sm:mb-6" [disabled]="saving() || nicknames().trim() === (auth.user()?.nicknames ?? '')">
              @if (saving()) {
                <hlm-spinner />
              }
              Save
            </button>
          </form>
        </section>
        <section hlmCard>
          <div hlmCardHeader>
            <h2 hlmCardTitle>Appearance</h2>
          </div>
          <div hlmCardContent>
            <hlm-toggle-group
              type="single"
              variant="outline"
              aria-label="Theme"
              [value]="theme.mode()"
              (valueChange)="setTheme($event)"
            >
              <button hlmToggleGroupItem value="light">Light</button>
              <button hlmToggleGroupItem value="dark">Dark</button>
              <button hlmToggleGroupItem value="system">System</button>
            </hlm-toggle-group>
          </div>
        </section>
        <section hlmCard>
          <div hlmCardHeader>
            <h2 hlmCardTitle>More</h2>
          </div>
          <div hlmCardContent class="flex flex-wrap gap-2">
            <a hlmBtn variant="outline" routerLink="/stickers">
              <ng-icon name="lucideSticker" />
              Stickers
            </a>
            <a hlmBtn variant="outline" [href]="githubUrl" target="_blank" rel="noopener">
              <ng-icon name="lucideGithub" />
              GitHub
            </a>
            <button hlmBtn variant="destructive" (click)="auth.logout()">
              <ng-icon name="lucideLogOut" />
              Log out
            </button>
          </div>
        </section>
      </div>
    </div>
  `,
})
export class Settings {
  protected readonly auth = inject(AuthService);
  protected readonly theme = inject(Theme);
  protected readonly githubUrl = 'https://github.com/PianoNic/NeuralDamage';
  protected readonly nicknames = signal('');
  protected readonly saving = signal(false);

  constructor() {
    // What is saved, once the account has loaded and after each save.
    effect(() => this.nicknames.set(this.auth.user()?.nicknames ?? ''));
  }

  protected async saveNicknames(): Promise<void> {
    this.saving.set(true);
    try {
      await this.auth.updateNicknames(this.nicknames());
      toast.success('Nicknames saved.');
    } catch (error) {
      toast.error(describeApiError(error, { fallback: 'Could not save the nicknames.' }));
    } finally {
      this.saving.set(false);
    }
  }

  protected setTheme(value: unknown): void {
    if (value === 'light' || value === 'dark' || value === 'system') this.theme.set(value as ThemeMode);
  }
}
