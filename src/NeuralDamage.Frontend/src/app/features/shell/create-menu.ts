import { Component, inject, input, signal } from '@angular/core';
import { Router } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideBot,
  lucideCircleDashed,
  lucideMessageCirclePlus,
  lucidePlus,
  lucideSquarePlus,
  lucideSticker,
} from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDrawerImports } from '@spartan-ng/helm/drawer';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmItemImports } from '@spartan-ng/helm/item';
import { HlmSidebarImports } from '@spartan-ng/helm/sidebar';
import { BotDialog } from '../bots/bot-dialog';
import { BotDirectory } from '../bots/bot-directory';
import { NewChatDialog } from '../chat/chat-dialogs';

type CreateAction = 'post' | 'story' | 'chat' | 'bot' | 'stickers';

interface CreateOption {
  action: CreateAction;
  label: string;
  description: string;
  icon: string;
}

const OPTIONS: readonly CreateOption[] = [
  { action: 'post', label: 'Post', description: 'Text, a picture or a sticker', icon: 'lucideSquarePlus' },
  { action: 'story', label: 'Story', description: 'Gone after 24 hours', icon: 'lucideCircleDashed' },
  { action: 'chat', label: 'Chat', description: 'Start one and bring some bots', icon: 'lucideMessageCirclePlus' },
  { action: 'bot', label: 'Bot', description: 'Describe it like a friend', icon: 'lucideBot' },
  { action: 'stickers', label: 'Stickers', description: 'Add to the shared library', icon: 'lucideSticker' },
];

/**
 * Create: a post, a story, a chat, a bot or stickers. In the desktop rail it is a menu item with a
 * dropdown; in the phone's bottom bar it is the square plus that opens a drawer.
 */
@Component({
  selector: 'app-create-menu',
  imports: [
    NgIcon,
    HlmButton,
    HlmDrawerImports,
    HlmDropdownMenuImports,
    HlmItemImports,
    HlmSidebarImports,
    BotDialog,
    NewChatDialog,
  ],
  providers: [
    provideIcons({
      lucideBot,
      lucideCircleDashed,
      lucideMessageCirclePlus,
      lucidePlus,
      lucideSquarePlus,
      lucideSticker,
    }),
  ],
  host: { class: 'contents' },
  template: `
    @if (place() === 'rail') {
      <button hlmSidebarMenuButton size="lg" class="gap-4 px-3 text-base [&_ng-icon]:text-[length:--spacing(6)] group-data-[collapsible=icon]:size-12! group-data-[collapsible=icon]:mx-auto group-data-[collapsible=icon]:justify-center group-data-[collapsible=icon]:[&>span]:hidden" [hlmDropdownMenuTrigger]="menu" side="right" align="start">
        <ng-icon name="lucideSquarePlus" />
        <span>Create</span>
      </button>
      <ng-template #menu>
        <hlm-dropdown-menu class="w-72 p-1.5">
          <!-- New chat, Bots and Stickers have their own places in the rail. -->
          @for (option of railOptions; track option.action) {
            <button hlmDropdownMenuItem class="gap-3.5 px-3 py-2.5" (triggered)="run(option.action)">
              <ng-icon [name]="option.icon" size="22" />
              <span class="flex flex-col items-start text-start">
                <span class="font-medium">{{ option.label }}</span>
                <span class="text-muted-foreground text-xs">{{ option.description }}</span>
              </span>
            </button>
          }
        </hlm-dropdown-menu>
      </ng-template>
    } @else {
      <hlm-drawer>
        <button hlmDrawerTrigger hlmBtn size="icon-lg" aria-label="Create">
          <ng-icon name="lucidePlus" size="20" />
        </button>
        <hlm-drawer-content *hlmDrawerPortal="let ctx">
          <hlm-drawer-header>
            <h2 hlmDrawerTitle class="font-display text-xl font-extrabold">Create</h2>
            <p hlmDrawerDescription class="sr-only">Start a post, a story, a chat, a bot or add stickers.</p>
          </hlm-drawer-header>
          <div hlmItemGroup class="px-2 pb-6">
            @for (option of options; track option.action) {
              <button hlmItem class="gap-4 py-3" (click)="ctx.close(); run(option.action)">
                <div hlmItemMedia class="bg-muted size-12 translate-y-0! self-center! rounded-xl" aria-hidden="true">
                  <ng-icon [name]="option.icon" size="24" />
                </div>
                <div hlmItemContent>
                  <div hlmItemTitle class="text-base">{{ option.label }}</div>
                  <p hlmItemDescription class="text-sm">{{ option.description }}</p>
                </div>
              </button>
            }
          </div>
        </hlm-drawer-content>
      </hlm-drawer>
    }

    <app-new-chat-dialog [open]="creatingChat()" (closed)="creatingChat.set(false)" />
    <app-bot-dialog [open]="creatingBot()" (closed)="creatingBot.set(false)" (saved)="bots.reload()" />
  `,
})
export class CreateMenu {
  private readonly router = inject(Router);
  protected readonly bots = inject(BotDirectory);

  /** Where it sits: the desktop rail or the phone's bottom bar. */
  readonly place = input<'rail' | 'bar'>('rail');

  protected readonly options = OPTIONS;
  protected readonly railOptions = OPTIONS.filter((option) => option.action === 'post' || option.action === 'story');
  protected readonly creatingChat = signal(false);
  protected readonly creatingBot = signal(false);

  protected run(action: CreateAction): void {
    switch (action) {
      case 'post':
        void this.router.navigate(['/feed/new']);
        break;
      case 'story':
        void this.router.navigate(['/feed/story']);
        break;
      case 'chat':
        this.creatingChat.set(true);
        break;
      case 'bot':
        this.creatingBot.set(true);
        break;
      case 'stickers':
        void this.router.navigate(['/stickers']);
        break;
    }
  }
}
