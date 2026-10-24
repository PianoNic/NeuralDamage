import { Component, inject, model, signal } from '@angular/core';
import { Router } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideBot, lucideHouse, lucideSearch, lucideSettings, lucideSticker } from '@ng-icons/lucide';
import { BrnCommandEmpty, BrnCommandInput } from '@spartan-ng/brain/command';
import { HlmCommandImports } from '@spartan-ng/helm/command';
import { HlmKbdImports } from '@spartan-ng/helm/kbd';
import { ChatAvatar, chatTitle } from '../chat/chat-avatar';
import { ChatList } from '../chat/chat-list';

/**
 * The Ctrl K palette: jump to any chat by name, or to the other pages. Built on the command
 * primitives with its own search row, so it reads like a search box rather than a menu.
 */
@Component({
  selector: 'app-search-palette',
  imports: [NgIcon, BrnCommandEmpty, BrnCommandInput, HlmCommandImports, HlmKbdImports, ChatAvatar],
  providers: [provideIcons({ lucideBot, lucideHouse, lucideSearch, lucideSettings, lucideSticker })],
  host: {
    '(document:keydown.control.k)': 'toggle($event)',
    '(document:keydown.meta.k)': 'toggle($event)',
  },
  template: `
    <hlm-command-dialog
      title="Search"
      description="Find a chat or a page"
      [state]="open() ? 'open' : 'closed'"
      (stateChange)="open.set($event === 'open')"
      dialogContentClass="w-[min(35rem,calc(100vw-2rem))] gap-0 overflow-hidden rounded-2xl p-0 sm:max-w-none"
    >
      <hlm-command class="rounded-none p-0">
        <div class="flex items-center gap-2.5 border-b px-4 py-3">
          <ng-icon name="lucideSearch" size="20" class="text-muted-foreground shrink-0" />
          <input
            brnCommandInput
            class="placeholder:text-muted-foreground min-w-0 flex-1 bg-transparent text-[17px] outline-none"
            placeholder="Search chats and pages"
            aria-label="Search chats and pages"
            (input)="query.set($any($event.target).value)"
          />
          <kbd hlmKbd>Esc</kbd>
        </div>

        <div hlmCommandList class="max-h-[min(26rem,60dvh)] p-2">
          <div *brnCommandEmpty hlmCommandEmpty>Nothing matches.</div>
          @if (chatList.chats().length) {
            <div hlmCommandGroup>
              <div hlmCommandGroupLabel>Chats</div>
              @for (chat of chatList.chats(); track chat.id) {
                <button hlmCommandItem class="gap-3 px-3 py-2 text-start text-[15px]" [value]="titleOf(chat) + ' ' + chat.id" (selected)="go(['/chats', chat.id])">
                  <app-chat-avatar [chat]="chat" [px]="34" />
                  <span class="min-w-0 flex-1 truncate">
                    @for (part of partsOf(titleOf(chat)); track $index) {
                      @if (part.match) {
                        <strong>{{ part.text }}</strong>
                      } @else {
                        {{ part.text }}
                      }
                    }
                  </span>
                  <kbd hlmKbd class="hidden group-data-selected/command-item:inline-flex">Enter</kbd>
                </button>
              }
            </div>
          }
          <div hlmCommandGroup>
            <div hlmCommandGroupLabel>Pages</div>
            @for (page of pages; track page.label) {
              <button hlmCommandItem class="gap-3 px-3 py-2 text-start text-[15px]" [value]="page.label" (selected)="go(page.link)">
                <span class="flex size-[34px] shrink-0 items-center justify-center">
                  <ng-icon [name]="page.icon" size="20" />
                </span>
                <span class="flex-1">{{ page.label }}</span>
                <kbd hlmKbd class="hidden group-data-selected/command-item:inline-flex">Enter</kbd>
              </button>
            }
          </div>
        </div>

        <div class="text-muted-foreground flex items-center gap-4 border-t px-4 py-2.5 text-xs max-md:hidden">
          <span class="flex items-center gap-1.5"><kbd hlmKbd>↑↓</kbd> move</span>
          <span class="flex items-center gap-1.5"><kbd hlmKbd>Enter</kbd> open</span>
          <span class="ms-auto flex items-center gap-1.5"><kbd hlmKbd>Ctrl K</kbd> anywhere</span>
        </div>
      </hlm-command>
    </hlm-command-dialog>
  `,
})
export class SearchPalette {
  private readonly router = inject(Router);
  protected readonly chatList = inject(ChatList);

  readonly open = model(false);
  /** What is typed, to show the matched part of each chat's name in bold. */
  protected readonly query = signal('');

  protected readonly pages = [
    { label: 'Home', icon: 'lucideHouse', link: ['/'] },
    { label: 'Bots', icon: 'lucideBot', link: ['/bots'] },
    { label: 'Stickers', icon: 'lucideSticker', link: ['/stickers'] },
    { label: 'Settings', icon: 'lucideSettings', link: ['/settings'] },
  ];

  protected titleOf(chat: Parameters<typeof chatTitle>[0]): string {
    return chatTitle(chat);
  }

  /** The name cut around the first place the search matches, ignoring case. */
  protected partsOf(name: string): { text: string; match: boolean }[] {
    const query = this.query().trim();
    const at = query ? name.toLowerCase().indexOf(query.toLowerCase()) : -1;
    if (at < 0) return [{ text: name, match: false }];
    return [
      { text: name.slice(0, at), match: false },
      { text: name.slice(at, at + query.length), match: true },
      { text: name.slice(at + query.length), match: false },
    ].filter((part) => part.text);
  }

  protected toggle(event: Event): void {
    event.preventDefault();
    this.open.update((open) => !open);
    this.query.set('');
  }

  protected go(commands: string[]): void {
    this.open.set(false);
    void this.router.navigate(commands);
  }
}
