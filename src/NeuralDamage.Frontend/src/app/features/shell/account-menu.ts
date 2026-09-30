import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideChevronsUpDown,
  lucideGithub,
  lucideLogOut,
  lucideMonitor,
  lucideMoon,
  lucideSettings,
  lucideSun,
} from '@ng-icons/lucide';
import { HlmAvatarImports } from '@spartan-ng/helm/avatar';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmSidebarImports } from '@spartan-ng/helm/sidebar';
import { AuthService } from '../../core/auth/auth.service';
import { Theme, ThemeMode } from '../../core/theme';
import { initials } from '../../shared/initials';

const GITHUB_URL = 'https://github.com/PianoNic/NeuralDamage';

/** The account row at the foot of the sidebar and the menu it opens upwards, after helm's nav-user block. */
@Component({
  selector: 'app-account-menu',
  imports: [RouterLink, NgIcon, HlmAvatarImports, HlmDropdownMenuImports, HlmSidebarImports],
  providers: [
    provideIcons({
      lucideChevronsUpDown,
      lucideGithub,
      lucideLogOut,
      lucideMonitor,
      lucideMoon,
      lucideSettings,
      lucideSun,
    }),
  ],
  template: `
    <ul hlmSidebarMenu>
      <li hlmSidebarMenuItem>
        <button
          #trigger
          hlmSidebarMenuButton
          size="lg"
          [hlmDropdownMenuTrigger]="menu"
          side="top"
          align="start"
        >
          <hlm-avatar class="rounded-lg after:rounded-lg">
            @if (user()?.avatarUrl) {
              <img hlmAvatarImage [src]="user()!.avatarUrl" alt="" class="rounded-lg" />
            }
            <span hlmAvatarFallback class="rounded-lg text-xs font-medium">{{ initial() }}</span>
          </hlm-avatar>
          <div class="grid flex-1 text-start leading-tight">
            <span class="truncate font-medium">{{ auth.displayName() }}</span>
            <span class="text-muted-foreground truncate text-xs">{{ user()?.email }}</span>
          </div>
          <ng-icon name="lucideChevronsUpDown" class="ms-auto" />
        </button>
      </li>
    </ul>

    <ng-template #menu>
      <hlm-dropdown-menu [style.width.px]="trigger.offsetWidth" class="min-w-56">
        <hlm-dropdown-menu-label>
          <div class="flex items-center gap-2 text-start">
            <hlm-avatar class="rounded-lg after:rounded-lg">
              @if (user()?.avatarUrl) {
                <img hlmAvatarImage [src]="user()!.avatarUrl" alt="" class="rounded-lg" />
              }
              <span hlmAvatarFallback class="rounded-lg text-xs font-medium">{{ initial() }}</span>
            </hlm-avatar>
            <div class="grid flex-1 leading-tight">
              <span class="truncate font-medium">{{ auth.displayName() }}</span>
              <span class="text-muted-foreground truncate text-xs font-normal">{{ user()?.email }}</span>
            </div>
          </div>
        </hlm-dropdown-menu-label>
        <hlm-dropdown-menu-separator />
        <hlm-dropdown-menu-group>
          <hlm-dropdown-menu-label class="text-muted-foreground text-xs font-normal">
            Theme
          </hlm-dropdown-menu-label>
          @for (option of themes; track option.mode) {
            <button
              hlmDropdownMenuRadio
              [checked]="theme.mode() === option.mode"
              (triggered)="theme.set(option.mode)"
            >
              <ng-icon [name]="option.icon" />
              {{ option.label }}
              <hlm-dropdown-menu-radio-indicator />
            </button>
          }
        </hlm-dropdown-menu-group>
        <hlm-dropdown-menu-separator />
        <hlm-dropdown-menu-group>
          <a hlmDropdownMenuItem routerLink="/settings">
            <ng-icon name="lucideSettings" />
            Settings
          </a>
          <a hlmDropdownMenuItem [href]="githubUrl" target="_blank" rel="noopener">
            <ng-icon name="lucideGithub" />
            GitHub
          </a>
        </hlm-dropdown-menu-group>
        <hlm-dropdown-menu-separator />
        <button hlmDropdownMenuItem variant="destructive" (triggered)="auth.logout()">
          <ng-icon name="lucideLogOut" />
          Log out
        </button>
      </hlm-dropdown-menu>
    </ng-template>
  `,
})
export class AccountMenu {
  protected readonly auth = inject(AuthService);
  protected readonly theme = inject(Theme);

  protected readonly githubUrl = GITHUB_URL;
  protected readonly themes: readonly { mode: ThemeMode; label: string; icon: string }[] = [
    { mode: 'light', label: 'Light', icon: 'lucideSun' },
    { mode: 'dark', label: 'Dark', icon: 'lucideMoon' },
    { mode: 'system', label: 'System', icon: 'lucideMonitor' },
  ];

  protected readonly user = this.auth.user;
  protected readonly initial = computed(() => initials(this.auth.displayName()));
}
