import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideSearch, lucideSquarePen } from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmInputGroupImports } from '@spartan-ng/helm/input-group';
import { filter, map } from 'rxjs';
import { ChatInbox } from './chat-inbox';
import { NewChatDialog } from './chat-dialogs';

/**
 * `/chats`: the inbox, and the open chat beside it on desktop. On a phone it is one or the other:
 * the inbox at `/chats`, the chat full screen at `/chats/:chatId`.
 */
@Component({
  selector: 'app-chats-layout',
  imports: [RouterOutlet, NgIcon, HlmButton, HlmInputGroupImports, ChatInbox, NewChatDialog],
  providers: [provideIcons({ lucideSearch, lucideSquarePen })],
  host: { class: 'flex h-full min-h-0' },
  template: `
    <aside class="min-h-0 w-full flex-col border-e md:flex md:w-80 lg:w-96" [class.flex]="!open()" [class.hidden]="open()">
      <header class="flex h-14 shrink-0 items-center gap-2 ps-4 pe-2">
        <h1 class="font-display flex-1 text-2xl font-extrabold tracking-tight">Chats</h1>
        <button hlmBtn variant="ghost" size="icon" aria-label="New chat" (click)="creating.set(true)">
          <ng-icon name="lucideSquarePen" />
        </button>
      </header>
      <div class="px-4 pb-2">
        <div hlmInputGroup>
          <input hlmInputGroupInput type="search" placeholder="Search chats" aria-label="Search chats" (input)="query.set($any($event.target).value)" />
          <div hlmInputGroupAddon>
            <ng-icon name="lucideSearch" />
          </div>
        </div>
      </div>
      <app-chat-inbox class="min-h-0 flex-1 overflow-y-auto pb-4" [filter]="query()" />
    </aside>
    <section class="min-h-0 min-w-0 flex-1 flex-col md:flex" [class.flex]="open()" [class.hidden]="!open()">
      <router-outlet />
    </section>

    <app-new-chat-dialog [open]="creating()" (closed)="creating.set(false)" />
  `,
})
export class ChatsLayout {
  private readonly router = inject(Router);

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => this.router.url),
    ),
    { initialValue: this.router.url },
  );

  /** Whether a chat is open, which on a phone hides the inbox. */
  protected readonly open = computed(() => /^\/chats\/[^/?#]+/.test(this.url()));
  protected readonly query = signal('');
  protected readonly creating = signal(false);
}
