import { Component, computed, inject, input, output, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronLeft, lucideEllipsis, lucidePencil, lucideTrash2, lucideUsers } from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmSidebarImports, HlmSidebarService } from '@spartan-ng/helm/sidebar';
import { ChatMember } from '../../core/models';
import { MemberAvatar } from '../../shared/member-avatar';
import { ChatRef, DeleteChatDialog, RenameChatDialog } from './chat-dialogs';
import { ChatList } from './chat-list';

const MAX_AVATARS = 4;

/** The bar over a chat: sidebar toggle, name and head count, member avatars, People and the chat menu. */
@Component({
  selector: 'app-chat-header',
  imports: [
    NgIcon,
    HlmButton,
    HlmDropdownMenuImports,
    HlmSidebarImports,
    MemberAvatar,
    RenameChatDialog,
    DeleteChatDialog,
  ],
  providers: [provideIcons({ lucideChevronLeft, lucideEllipsis, lucidePencil, lucideTrash2, lucideUsers })],
  host: { class: 'flex h-12 shrink-0 items-center gap-2 border-b px-2 md:px-3' },
  template: `
    @if (sidebar.isMobile()) {
      <button hlmBtn variant="ghost" size="icon" aria-label="Chats" (click)="sidebar.setOpenMobile(true)">
        <ng-icon name="lucideChevronLeft" />
      </button>
    } @else {
      <button hlmSidebarTrigger></button>
    }

    <div class="flex min-w-0 flex-1 flex-col md:flex-row md:items-baseline md:gap-2">
      <h1 class="truncate text-sm font-semibold">{{ name() }}</h1>
      <p class="text-muted-foreground truncate text-xs">{{ headCount() }}</p>
    </div>

    <div class="hidden -space-x-1.5 md:flex" aria-hidden="true">
      @for (member of shownMembers(); track member.id) {
        <span class="ring-background flex rounded-full ring-2" [title]="member.displayName">
          <app-member-avatar [name]="member.displayName" [avatarUrl]="member.avatarUrl" [modelId]="member.modelId" [px]="24" />
        </span>
      }
    </div>

    <button hlmBtn variant="outline" size="sm" class="hidden md:inline-flex" (click)="people.emit()">
      <ng-icon name="lucideUsers" />
      People
    </button>
    <button hlmBtn variant="ghost" size="icon" class="md:hidden" aria-label="People" (click)="people.emit()">
      <ng-icon name="lucideUsers" />
    </button>

    <!-- Rename and delete are the owner's; the server refuses them to anyone else. -->
    @if (chatList.owns(chatId())) {
      <button hlmBtn variant="ghost" size="icon" aria-label="Chat menu" [hlmDropdownMenuTrigger]="menu" align="end">
        <ng-icon name="lucideEllipsis" />
      </button>
    }
    <ng-template #menu>
      <hlm-dropdown-menu class="w-44">
        <button hlmDropdownMenuItem (triggered)="renaming.set(chatRef())">
          <ng-icon name="lucidePencil" />
          Rename
        </button>
        <button hlmDropdownMenuItem variant="destructive" (triggered)="deleting.set(chatRef())">
          <ng-icon name="lucideTrash2" />
          Delete
        </button>
      </hlm-dropdown-menu>
    </ng-template>

    <app-rename-chat-dialog [chat]="renaming()" (closed)="renaming.set(null)" />
    <app-delete-chat-dialog [chat]="deleting()" (closed)="deleting.set(null)" />
  `,
})
export class ChatHeader {
  protected readonly sidebar = inject(HlmSidebarService);
  protected readonly chatList = inject(ChatList);

  readonly chatId = input.required<string>();
  readonly name = input.required<string>();
  readonly members = input.required<ChatMember[]>();
  readonly people = output();

  protected readonly renaming = signal<ChatRef | null>(null);
  protected readonly deleting = signal<ChatRef | null>(null);
  protected readonly chatRef = computed<ChatRef>(() => ({ id: this.chatId(), name: this.name() }));

  protected readonly headCount = computed(() => {
    const bots = this.members().filter((m) => m.memberType === 'bot').length;
    const people = this.members().length - bots;
    return `${bots} ${bots === 1 ? 'bot' : 'bots'}, ${people} ${people === 1 ? 'person' : 'people'}`;
  });

  /** Bots first, since they are who a chat is about. */
  protected readonly shownMembers = computed(() =>
    [...this.members()]
      .sort((a, b) => (a.memberType === b.memberType ? 0 : a.memberType === 'bot' ? -1 : 1))
      .slice(0, MAX_AVATARS),
  );
}
