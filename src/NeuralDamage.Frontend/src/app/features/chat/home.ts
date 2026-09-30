import { afterNextRender, Component, inject, signal } from '@angular/core';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { HlmSidebarImports, HlmSidebarService } from '@spartan-ng/helm/sidebar';
import { Logo } from '../../shared/logo';
import { NewChatDialog } from './chat-dialogs';

/** `/`: nothing open yet. On a phone the chat list is the home screen, so the sidebar opens. */
@Component({
  selector: 'app-home',
  imports: [HlmButton, HlmEmptyImports, HlmSidebarImports, Logo, NewChatDialog],
  host: { class: 'flex h-full min-h-0 flex-col' },
  template: `
    <header class="flex h-12 shrink-0 items-center border-b px-2 md:px-3">
      <button hlmSidebarTrigger></button>
    </header>
    <hlm-empty class="flex-1">
      <hlm-empty-header>
        <app-logo [size]="56" class="mb-2" />
        <h1 hlmEmptyTitle>Neural Damage</h1>
        <p hlmEmptyDescription>Pick a chat from the sidebar, or start a new one and bring some bots.</p>
      </hlm-empty-header>
      <button hlmBtn (click)="creating.set(true)">New chat</button>
    </hlm-empty>
    <app-new-chat-dialog [open]="creating()" (closed)="creating.set(false)" />
  `,
})
export class Home {
  protected readonly creating = signal(false);

  constructor() {
    const sidebar = inject(HlmSidebarService);
    afterNextRender(() => {
      if (sidebar.isMobile()) sidebar.setOpenMobile(true);
    });
  }
}
