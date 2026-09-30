import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideBot, lucidePlus, lucideSearch } from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmKbdImports } from '@spartan-ng/helm/kbd';
import { HlmSidebarImports, HlmSidebarService } from '@spartan-ng/helm/sidebar';
import { filter, map } from 'rxjs';
import { APP_VERSION } from '../../core/version';
import { Logo } from '../../shared/logo';
import { BotDirectory } from '../bots/bot-directory';
import { NewChatDialog } from '../chat/chat-dialogs';
import { ChatList } from '../chat/chat-list';
import { AccountMenu } from './account-menu';
import { ChatNav } from './chat-nav';
import { SearchPalette } from './search-palette';

/**
 * The sidebar: logo and version, New chat, Search and Bots, the chats, and the account menu. Hosts
 * the page next to it, since helm's inset panel styles itself from the sidebar as its sibling.
 */
@Component({
  selector: 'app-sidebar',
  imports: [
    RouterLink,
    NgIcon,
    HlmButton,
    HlmKbdImports,
    HlmSidebarImports,
    Logo,
    AccountMenu,
    ChatNav,
    NewChatDialog,
    SearchPalette,
  ],
  providers: [provideIcons({ lucideBot, lucidePlus, lucideSearch })],
  template: `
    <div hlmSidebarWrapper class="h-svh">
      <hlm-sidebar variant="inset" sidebarWidthMobile="100vw">
        <hlm-sidebar-header>
          <a routerLink="/" class="flex items-center gap-2 rounded-md p-2 outline-none focus-visible:ring-2 focus-visible:ring-sidebar-ring">
            <app-logo [size]="32" />
            <span class="flex flex-col leading-tight">
              <span class="text-sm font-semibold">Neural Damage</span>
              <span class="text-muted-foreground text-xs">v{{ version }}</span>
            </span>
          </a>
          <div class="px-2">
            <button hlmBtn class="w-full" (click)="openCreate()">
              <ng-icon name="lucidePlus" />
              New chat
            </button>
          </div>
          <ul hlmSidebarMenu>
            <li hlmSidebarMenuItem>
              <button hlmSidebarMenuButton (click)="openSearch()">
                <ng-icon name="lucideSearch" />
                <span>Search</span>
              </button>
              <kbd hlmKbd class="pointer-events-none absolute end-1 top-1.5 hidden md:inline-flex">Ctrl K</kbd>
            </li>
            <li hlmSidebarMenuItem>
              <a hlmSidebarMenuButton closeMobileSidebarOnClick routerLink="/bots" [isActive]="path() === '/bots'">
                <ng-icon name="lucideBot" />
                <span>Bots</span>
              </a>
              @if (bots.bots().length) {
                <div hlmSidebarMenuBadge class="text-muted-foreground">{{ bots.bots().length }}</div>
              }
            </li>
          </ul>
        </hlm-sidebar-header>

        <hlm-sidebar-content>
          <app-chat-nav />
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
  private readonly router = inject(Router);
  private readonly sidebar = inject(HlmSidebarService);
  private readonly chatList = inject(ChatList);
  protected readonly bots = inject(BotDirectory);

  protected readonly version = APP_VERSION;
  protected readonly creating = signal(false);
  protected readonly searching = signal(false);

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => this.router.url),
    ),
    { initialValue: this.router.url },
  );
  protected readonly path = computed(() => this.url().split(/[?#]/)[0]);

  ngOnInit(): void {
    void this.chatList.start();
    void this.bots.load();
  }

  /** On phones the sidebar is a sheet; opening the dialog from it closes the sheet first. */
  protected openCreate(): void {
    this.sidebar.setOpenMobile(false);
    this.creating.set(true);
  }

  protected openSearch(): void {
    this.sidebar.setOpenMobile(false);
    this.searching.set(true);
  }
}
