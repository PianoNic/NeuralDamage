import { Component, inject } from '@angular/core';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmFieldImports } from '@spartan-ng/helm/field';
import { HlmSidebarImports } from '@spartan-ng/helm/sidebar';
import { HlmToggleGroupImports } from '@spartan-ng/helm/toggle-group';
import { AuthService } from '../../core/auth/auth.service';
import { Theme, ThemeMode } from '../../core/theme';

/** `/settings`: the account as the identity provider reports it, and the theme. */
@Component({
  selector: 'app-settings',
  imports: [HlmCardImports, HlmFieldImports, HlmSidebarImports, HlmToggleGroupImports],
  host: { class: 'flex h-full min-h-0 flex-col' },
  template: `
    <header class="flex h-12 shrink-0 items-center gap-2 border-b px-2 md:px-3">
      <button hlmSidebarTrigger></button>
      <h1 class="text-sm font-semibold">Settings</h1>
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
      </div>
    </div>
  `,
})
export class Settings {
  protected readonly auth = inject(AuthService);
  protected readonly theme = inject(Theme);

  protected setTheme(value: unknown): void {
    if (value === 'light' || value === 'dark' || value === 'system') this.theme.set(value as ThemeMode);
  }
}
