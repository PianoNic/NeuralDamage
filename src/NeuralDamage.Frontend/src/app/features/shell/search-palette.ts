import { Component, inject, model } from '@angular/core';
import { Router } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideBot, lucideMessageSquare, lucideSettings } from '@ng-icons/lucide';
import { BrnCommandEmpty } from '@spartan-ng/brain/command';
import { HlmCommandImports } from '@spartan-ng/helm/command';
import { HlmSidebarService } from '@spartan-ng/helm/sidebar';
import { ChatList } from '../chat/chat-list';

/** The Ctrl K palette: jump to any chat by name, or to the other pages. */
@Component({
  selector: 'app-search-palette',
  imports: [NgIcon, BrnCommandEmpty, HlmCommandImports],
  providers: [provideIcons({ lucideBot, lucideMessageSquare, lucideSettings })],
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
      dialogContentClass="sm:max-w-lg w-[calc(100%-2rem)]"
    >
      <hlm-command>
        <hlm-command-input placeholder="Search chats…" />
        <div hlmCommandList>
          <div *brnCommandEmpty hlmCommandEmpty>No chats match.</div>
          @if (chatList.chats().length) {
            <div hlmCommandGroup>
              <div hlmCommandGroupLabel>Chats</div>
              @for (chat of chatList.chats(); track chat.id) {
                <button hlmCommandItem [value]="chat.name + ' ' + chat.id" (selected)="go(['/chat', chat.id])">
                  <ng-icon name="lucideMessageSquare" />
                  <span class="truncate">{{ chat.name }}</span>
                </button>
              }
            </div>
          }
          <div hlmCommandGroup>
            <div hlmCommandGroupLabel>Pages</div>
            <button hlmCommandItem value="Bots" (selected)="go(['/bots'])">
              <ng-icon name="lucideBot" />
              Bots
            </button>
            <button hlmCommandItem value="Settings" (selected)="go(['/settings'])">
              <ng-icon name="lucideSettings" />
              Settings
            </button>
          </div>
        </div>
      </hlm-command>
    </hlm-command-dialog>
  `,
})
export class SearchPalette {
  private readonly router = inject(Router);
  private readonly sidebar = inject(HlmSidebarService);
  protected readonly chatList = inject(ChatList);

  readonly open = model(false);

  protected toggle(event: Event): void {
    event.preventDefault();
    this.open.update((open) => !open);
  }

  protected go(commands: string[]): void {
    this.open.set(false);
    this.sidebar.setOpenMobile(false);
    void this.router.navigate(commands);
  }
}
