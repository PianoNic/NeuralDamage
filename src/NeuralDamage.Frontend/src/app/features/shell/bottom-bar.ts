import { Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideBot, lucideHouse, lucideMessageCircle } from '@ng-icons/lucide';
import { HlmAvatarImports } from '@spartan-ng/helm/avatar';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { AuthService } from '../../core/auth/auth.service';
import { initials } from '../../shared/initials';
import { ChatList } from '../chat/chat-list';
import { CreateMenu } from './create-menu';

/** The phone's tab bar, after Instagram's: Home, Bots, Create, Chats and your profile. */
@Component({
  selector: 'app-bottom-bar',
  imports: [RouterLink, NgIcon, HlmAvatarImports, HlmBadge, HlmButton, CreateMenu],
  providers: [provideIcons({ lucideBot, lucideHouse, lucideMessageCircle })],
  host: { class: 'bg-background grid box-content h-14 shrink-0 grid-cols-5 place-items-center border-t pb-[env(safe-area-inset-bottom)]' },
  template: `
    <nav aria-label="Main" class="contents">
      <a hlmBtn [variant]="path() === '/' ? 'secondary' : 'ghost'" size="icon-lg" routerLink="/" aria-label="Home" [attr.aria-current]="path() === '/' ? 'page' : null">
        <ng-icon name="lucideHouse" size="24" />
      </a>
      <a hlmBtn [variant]="path().startsWith('/bots') ? 'secondary' : 'ghost'" size="icon-lg" routerLink="/bots" aria-label="Bots" [attr.aria-current]="path().startsWith('/bots') ? 'page' : null">
        <ng-icon name="lucideBot" size="24" />
      </a>
      <div class="flex justify-center">
        <app-create-menu place="bar" />
      </div>
      <a
        hlmBtn
        [variant]="path().startsWith('/chats') ? 'secondary' : 'ghost'"
        size="icon-lg"
        routerLink="/chats"
        class="relative"
        [attr.aria-label]="unread() ? 'Chats, ' + unread() + ' unread' : 'Chats'"
        [attr.aria-current]="path().startsWith('/chats') ? 'page' : null"
      >
        <ng-icon name="lucideMessageCircle" size="24" />
        @if (unread()) {
          <span hlmBadge class="bg-highlight text-highlight-foreground absolute -end-1 -bottom-1 h-5 min-w-5 px-1 tabular-nums" aria-hidden="true">
            {{ unread() > 99 ? '99+' : unread() }}
          </span>
        }
      </a>
      <a hlmBtn [variant]="path().startsWith('/me') || path().startsWith('/settings') ? 'secondary' : 'ghost'" size="icon-lg" routerLink="/me" aria-label="Your profile">
        <hlm-avatar class="size-7">
          @if (auth.user()?.avatarUrl) {
            <img hlmAvatarImage [src]="auth.user()!.avatarUrl" alt="" />
          }
          <span hlmAvatarFallback class="text-[10px] font-medium">{{ initial() }}</span>
        </hlm-avatar>
      </a>
    </nav>
  `,
})
export class BottomBar {
  protected readonly auth = inject(AuthService);
  private readonly chatList = inject(ChatList);

  /** The current path, without query or fragment. */
  readonly path = input.required<string>();

  protected readonly initial = computed(() => initials(this.auth.displayName()));
  protected readonly unread = computed(() => {
    let total = 0;
    for (const count of this.chatList.unread().values()) total += count;
    return total;
  });
}
