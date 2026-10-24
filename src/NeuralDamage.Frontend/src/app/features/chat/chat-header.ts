import { Component, computed, inject, input, output, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronLeft, lucideEllipsis, lucidePencil, lucideTrash2, lucideUsers } from '@ng-icons/lucide';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { RouterLink } from '@angular/router';
import { ChatMember } from '../../core/models';
import { MemberAvatar } from '../../shared/member-avatar';
import { ChatRef, DeleteChatDialog, EditChatDialog } from './chat-dialogs';
import { ChatList } from './chat-list';

const MAX_AVATARS = 4;

/** The bar over a chat: back to the inbox on phones, name and head count, member avatars, People and the chat menu. */
@Component({
  selector: 'app-chat-header',
  imports: [
    NgIcon,
    HlmBadge,
    HlmButton,
    HlmDropdownMenuImports,
    RouterLink,
    MemberAvatar,
    EditChatDialog,
    DeleteChatDialog,
  ],
  providers: [provideIcons({ lucideChevronLeft, lucideEllipsis, lucidePencil, lucideTrash2, lucideUsers })],
  host: { class: 'flex h-14 shrink-0 items-center gap-2 border-b px-2 md:px-3' },
  template: `
    <a hlmBtn variant="ghost" size="icon" class="md:hidden" routerLink="/chats" aria-label="Back to chats">
      <ng-icon name="lucideChevronLeft" />
    </a>

    @if (other(); as them) {
      <!-- A direct chat is shown as who it is with, and leads to their profile when that is a bot. -->
      <a
        class="flex min-w-0 flex-1 items-center gap-2.5 rounded-md outline-none focus-visible:ring-2 focus-visible:ring-ring/50"
        [routerLink]="them.botId ? ['/bots', them.botId] : null"
      >
        <app-member-avatar [name]="them.displayName" [avatarUrl]="them.avatarUrl" [modelId]="them.modelId" [px]="32" />
        <h1 class="truncate text-sm font-semibold">{{ them.displayName }}</h1>
        @if (them.memberType === 'bot') {
          <span hlmBadge class="bg-bot text-bot-foreground h-4 rounded px-1 text-[10px] font-extrabold tracking-wide">BOT</span>
        }
      </a>
    } @else {
      @if (avatarUrl(); as url) {
        <app-member-avatar class="shrink-0" [name]="name()" [avatarUrl]="url" [px]="32" />
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
    }

    <!-- A direct chat is just the two of you, so there is nobody to list. -->
    @if (!direct()) {
      <button hlmBtn variant="outline" size="sm" class="hidden md:inline-flex" (click)="people.emit()">
        <ng-icon name="lucideUsers" />
        People
      </button>
      <button hlmBtn variant="ghost" size="icon" class="md:hidden" aria-label="People" (click)="people.emit()">
        <ng-icon name="lucideUsers" />
      </button>
    }

    <!-- Rename and delete are the owner's; the server refuses them to anyone else. -->
    @if (chatList.owns(chatId())) {
      <button hlmBtn variant="ghost" size="icon" aria-label="Chat menu" [hlmDropdownMenuTrigger]="menu" align="end">
        <ng-icon name="lucideEllipsis" />
      </button>
    }
    <ng-template #menu>
      <hlm-dropdown-menu class="w-44">
        <!-- A direct chat is named after who it is with. -->
        @if (!direct()) {
          <button hlmDropdownMenuItem (triggered)="editing.set(chatRef())">
            <ng-icon name="lucidePencil" />
            Edit
          </button>
        }
        <button hlmDropdownMenuItem variant="destructive" (triggered)="deleting.set(chatRef())">
          <ng-icon name="lucideTrash2" />
          Delete
        </button>
      </hlm-dropdown-menu>
    </ng-template>

    <app-edit-chat-dialog [chat]="editing()" (closed)="editing.set(null)" />
    <app-delete-chat-dialog [chat]="deleting()" (closed)="deleting.set(null)" />
  `,
})
export class ChatHeader {
  protected readonly chatList = inject(ChatList);

  readonly chatId = input.required<string>();
  readonly name = input.required<string>();
  readonly members = input.required<ChatMember[]>();
  /** Just you and one bot or person. */
  readonly direct = input(false);
  /** A group chat's own picture. */
  readonly avatarUrl = input<string | null | undefined>(null);
  readonly currentUserId = input<string | null>(null);
  readonly people = output();

  protected readonly other = computed(() =>
    this.direct() ? (this.members().find((m) => m.userId !== this.currentUserId()) ?? null) : null,
  );

  protected readonly editing = signal<ChatRef | null>(null);
  protected readonly deleting = signal<ChatRef | null>(null);
  protected readonly chatRef = computed<ChatRef>(() => ({ id: this.chatId(), name: this.other()?.displayName ?? this.name(), avatarUrl: this.avatarUrl() }));

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
