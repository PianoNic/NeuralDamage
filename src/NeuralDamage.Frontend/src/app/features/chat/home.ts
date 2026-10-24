import { Component, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideMessageCircle } from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { NewChatDialog } from './chat-dialogs';

/** `/chats` with no chat open: on desktop the space beside the inbox. Phones show only the inbox. */
@Component({
  selector: 'app-home',
  imports: [NgIcon, HlmButton, HlmEmptyImports, NewChatDialog],
  providers: [provideIcons({ lucideMessageCircle })],
  host: { class: 'flex h-full min-h-0 flex-col' },
  template: `
    <hlm-empty class="flex-1">
      <hlm-empty-header>
        <div hlmEmptyMedia variant="icon">
          <ng-icon name="lucideMessageCircle" />
        </div>
        <h1 hlmEmptyTitle class="font-display text-2xl font-extrabold">Your chats</h1>
        <p hlmEmptyDescription>Pick a chat, or start a new one and bring some bots.</p>
      </hlm-empty-header>
      <button hlmBtn (click)="creating.set(true)">New chat</button>
    </hlm-empty>
    <app-new-chat-dialog [open]="creating()" (closed)="creating.set(false)" />
  `,
})
export class Home {
  protected readonly creating = signal(false);
}
