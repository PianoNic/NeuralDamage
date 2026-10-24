import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideGithub, lucideLogOut, lucideSticker } from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmToggleGroupImports } from '@spartan-ng/helm/toggle-group';
import { AuthService } from '../../core/auth/auth.service';
import { Theme, ThemeMode } from '../../core/theme';

/**
 * `/settings`: the account as the identity provider reports it, the theme, and on phones, where the
 * rail's account menu is missing, the way to stickers, GitHub and logging out.
 */
@Component({
  selector: 'app-settings',
  imports: [RouterLink, NgIcon, HlmButton, HlmCardImports, HlmFieldImports, HlmToggleGroupImports],
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

  protected setTheme(value: unknown): void {
    if (value === 'light' || value === 'dark' || value === 'system') this.theme.set(value as ThemeMode);
  }
}
