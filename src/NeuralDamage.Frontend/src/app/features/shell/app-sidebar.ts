import { Component, computed, effect, inject, input, OnInit, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideBot, lucideHouse, lucideMessageCircle, lucidePanelLeft, lucidePlus, lucideSearch, lucideSticker } from '@ng-icons/lucide';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmKbdImports } from '@spartan-ng/helm/kbd';
import { HlmSidebarImports, HlmSidebarService } from '@spartan-ng/helm/sidebar';
import { AppConfig } from '../../core/app-config';
import { BotDirectory } from '../bots/bot-directory';
import { NewChatDialog } from '../chat/chat-dialogs';
import { ChatList } from '../chat/chat-list';
import { FeedStore } from '../feed/feed-store';
import { StickerLibrary } from '../stickers/sticker-library';
import { AccountMenu } from './account-menu';
import { CreateMenu } from './create-menu';
import { SearchPalette } from './search-palette';

/**
 * The desktop rail: the wordmark, New chat, then Home, Search, Chats, Bots, Stickers and Create,
 * and the account menu at the foot. Phones get the bottom bar instead, so the rail never opens as
 * a sheet there. Hosts the page next to it, since helm's inset styles itself from the sidebar.
 *
 * It folds to its icons with the button by the wordmark or Ctrl B, and on its own in Chats, where
 * the inbox and the chat want the room, as Instagram does for messages.
 */
@Component({
  selector: 'app-sidebar',
  imports: [
    RouterLink,
    NgIcon,
    HlmBadge,
    HlmButton,
    HlmKbdImports,
    HlmSidebarImports,
    AccountMenu,
    CreateMenu,
    NewChatDialog,
    SearchPalette,
  ],
  providers: [provideIcons({ lucideBot, lucideHouse, lucideMessageCircle, lucidePanelLeft, lucidePlus, lucideSearch, lucideSticker })],
  template: `
    <div hlmSidebarWrapper sidebarWidthIcon="4.5rem" class="h-svh">
      <hlm-sidebar variant="sidebar" collapsible="icon" class="max-md:hidden">
        <hlm-sidebar-header class="gap-3 pt-5">
          <div class="flex items-start gap-1 group-data-[collapsible=icon]:justify-center">
            <a routerLink="/" class="min-w-0 flex-1 rounded-md px-2 outline-none focus-visible:ring-2 focus-visible:ring-sidebar-ring group-data-[collapsible=icon]:hidden">
              <span class="font-display block truncate text-2xl font-extrabold tracking-tight">Neural Damage</span>
              @if (version(); as version) {
                <span class="text-muted-foreground text-xs">v{{ version }}</span>
              }
            </a>
            <button
              hlmBtn
              variant="ghost"
              size="icon-lg"
              class="text-muted-foreground shrink-0"
              [attr.aria-label]="sidebar.open() ? 'Collapse the sidebar' : 'Expand the sidebar'"
              [attr.aria-expanded]="sidebar.open()"
              title="Ctrl B"
              (click)="sidebar.toggleSidebar()"
            >
              <ng-icon name="lucidePanelLeft" size="20" />
            </button>
          </div>
          <button
            hlmBtn
            size="lg"
            class="w-full group-data-[collapsible=icon]:size-12 group-data-[collapsible=icon]:self-center group-data-[collapsible=icon]:p-0"
            aria-label="New chat"
            (click)="creating.set(true)"
          >
            <ng-icon name="lucidePlus" />
            <span class="group-data-[collapsible=icon]:hidden">New chat</span>
          </button>
        </hlm-sidebar-header>

        <hlm-sidebar-content>
          <hlm-sidebar-group>
            <ul hlmSidebarMenu>
              <li hlmSidebarMenuItem>
                <a hlmSidebarMenuButton size="lg" class="gap-4 px-3 text-base [&_ng-icon]:text-[length:--spacing(6)] group-data-[collapsible=icon]:size-12! group-data-[collapsible=icon]:mx-auto group-data-[collapsible=icon]:justify-center group-data-[collapsible=icon]:[&>span]:hidden" routerLink="/" [isActive]="path() === '/'">
                  <ng-icon name="lucideHouse" />
                  <span>Home</span>
                </a>
                @if (feed.unread() && path() !== '/') {
                  <div hlmSidebarMenuBadge class="end-3 top-1/2! -translate-y-1/2">
                    <span hlmBadge class="bg-highlight text-highlight-foreground tabular-nums" data-testid="feed-unread">
                      {{ feed.unread() > 99 ? '99+' : feed.unread() }}
                    </span>
                    <span class="sr-only">new on the feed</span>
                  </div>
                }
              </li>
              <li hlmSidebarMenuItem>
                <button hlmSidebarMenuButton size="lg" class="gap-4 px-3 text-base [&_ng-icon]:text-[length:--spacing(6)] group-data-[collapsible=icon]:size-12! group-data-[collapsible=icon]:mx-auto group-data-[collapsible=icon]:justify-center group-data-[collapsible=icon]:[&>span]:hidden" (click)="searching.set(true)">
                  <ng-icon name="lucideSearch" />
                  <span>Search</span>
                </button>
                <div hlmSidebarMenuBadge class="end-3 top-1/2! -translate-y-1/2"><kbd hlmKbd>Ctrl K</kbd></div>
              </li>
              <li hlmSidebarMenuItem>
                <a hlmSidebarMenuButton size="lg" class="gap-4 px-3 text-base [&_ng-icon]:text-[length:--spacing(6)] group-data-[collapsible=icon]:size-12! group-data-[collapsible=icon]:mx-auto group-data-[collapsible=icon]:justify-center group-data-[collapsible=icon]:[&>span]:hidden" routerLink="/chats" [isActive]="path().startsWith('/chats')">
                  <ng-icon name="lucideMessageCircle" />
                  <span>Chats</span>
                </a>
                @if (unread()) {
                  <div hlmSidebarMenuBadge class="end-3 top-1/2! -translate-y-1/2">
                    <span hlmBadge class="bg-highlight text-highlight-foreground tabular-nums">{{ unread() > 99 ? '99+' : unread() }}</span>
                    <span class="sr-only">unread</span>
                  </div>
                }
              </li>
              <li hlmSidebarMenuItem>
                <a hlmSidebarMenuButton size="lg" class="gap-4 px-3 text-base [&_ng-icon]:text-[length:--spacing(6)] group-data-[collapsible=icon]:size-12! group-data-[collapsible=icon]:mx-auto group-data-[collapsible=icon]:justify-center group-data-[collapsible=icon]:[&>span]:hidden" routerLink="/bots" [isActive]="path().startsWith('/bots')">
                  <ng-icon name="lucideBot" />
                  <span>Bots</span>
                </a>
                @if (bots.bots().length) {
                  <div hlmSidebarMenuBadge class="text-muted-foreground end-3 top-1/2! -translate-y-1/2">{{ bots.bots().length }}</div>
                }
              </li>
              <li hlmSidebarMenuItem>
                <a hlmSidebarMenuButton size="lg" class="gap-4 px-3 text-base [&_ng-icon]:text-[length:--spacing(6)] group-data-[collapsible=icon]:size-12! group-data-[collapsible=icon]:mx-auto group-data-[collapsible=icon]:justify-center group-data-[collapsible=icon]:[&>span]:hidden" routerLink="/stickers" [isActive]="path().startsWith('/stickers')">
                  <ng-icon name="lucideSticker" />
                  <span>Stickers</span>
                </a>
                @if (stickers.count()) {
                  <div hlmSidebarMenuBadge class="text-muted-foreground end-3 top-1/2! -translate-y-1/2">{{ stickers.count() }}</div>
                }
              </li>
              <li hlmSidebarMenuItem>
                <app-create-menu place="rail" />
              </li>
            </ul>
          </hlm-sidebar-group>
        </hlm-sidebar-content>

        <hlm-sidebar-footer>
          <app-account-menu />
        </hlm-sidebar-footer>
      </hlm-sidebar>
      <ng-content />
    </div>

    <app-new-chat-dialog [open]="creating()" (closed)="creating.set(false)" />
    <app-search-palette [(open)]="searching" />
  `,
})
export class AppSidebar implements OnInit {
  private readonly chatList = inject(ChatList);
  protected readonly bots = inject(BotDirectory);
  protected readonly stickers = inject(StickerLibrary);
  protected readonly feed = inject(FeedStore);

  /** The current path, without query or fragment. */
  readonly path = input.required<string>();

  /** The running server's release, from `/api/app`. */
  protected readonly version = inject(AppConfig).version;
  protected readonly creating = signal(false);
  protected readonly searching = signal(false);
  protected readonly sidebar = inject(HlmSidebarService);

  private readonly inChats = computed(() => this.path().startsWith('/chats'));
  /** Whether the rail is open outside Chats: what it was left at there, remembered per browser. */
  private openElsewhere = readRail();

  constructor() {
    // Chats folds it; anywhere else it is as it was left there. Opening it in Chats is just for then.
    effect(() => {
      const inChats = this.inChats();
      untracked(() => this.sidebar.setOpen(inChats ? false : this.openElsewhere));
    });
    effect(() => {
      const open = this.sidebar.open();
      if (untracked(this.inChats) || open === this.openElsewhere) return;
      this.openElsewhere = open;
      saveRail(open);
    });
  }

  protected readonly unread = computed(() => {
    let total = 0;
    for (const count of this.chatList.unread().values()) total += count;
    return total;
  });

  ngOnInit(): void {
    void this.chatList.start();
    void this.bots.load();
    void this.stickers.load();
    void this.feed.start();
  }
}

const RAIL_KEY = 'nd.rail-open';

function readRail(): boolean {
  try {
    return localStorage.getItem(RAIL_KEY) !== 'false';
  } catch {
    return true;
  }
}

function saveRail(open: boolean): void {
  try {
    localStorage.setItem(RAIL_KEY, String(open));
  } catch {
    // Private mode: it opens again next time.
  }
}
