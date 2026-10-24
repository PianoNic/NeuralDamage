import { Component, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideEllipsis, lucideLogOut, lucideMessageCircle, lucidePencil, lucideTrash2 } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertDialogImports } from '@spartan-ng/helm/alert-dialog';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { HlmItemImports } from '@spartan-ng/helm/item';
import { HlmSkeleton } from '@spartan-ng/helm/skeleton';
import { describeApiError } from '../../core/http-errors';
import { formatAgo } from '../feed/feed-model';
import { ChatDto } from '../../core/models';
import { ChatRef, DeleteChatDialog, EditChatDialog } from './chat-dialogs';
import { groupChatsByDay } from './chat-groups';
import { ChatAvatar, chatTitle } from './chat-avatar';
import { ChatList } from './chat-list';

const SKELETON_ROWS = [0, 1, 2, 3, 4];

/** The chats, grouped Today / Earlier, with unread counts and a rename/delete menu for owners. */
@Component({
  selector: 'app-chat-inbox',
  imports: [
    RouterLink,
    NgIcon,
    HlmBadge,
    HlmButton,
    HlmDropdownMenuImports,
    HlmEmptyImports,
    HlmItemImports,
    HlmSkeleton,
    ChatAvatar,
    EditChatDialog,
    HlmAlertDialogImports,
    DeleteChatDialog,
  ],
  providers: [provideIcons({ lucideEllipsis, lucideLogOut, lucideMessageCircle, lucidePencil, lucideTrash2 })],
  host: { class: 'flex flex-col' },
  template: `
    @if (chatList.loading() && !chatList.chats().length) {
      <div class="flex flex-col gap-3 px-4 py-2">
        @for (row of skeletonRows; track row) {
          <div class="flex items-center gap-3">
            <hlm-skeleton class="size-12 rounded-full" />
            <hlm-skeleton class="h-4 flex-1" />
          </div>
        }
      </div>
    } @else {
      @for (group of groups(); track group.label) {
        <h2 class="text-muted-foreground px-4 pt-3 pb-1 text-xs font-semibold">{{ group.label }}</h2>
        <div hlmItemGroup class="px-2">
          @for (chat of group.chats; track chat.id) {
            <div hlmItem size="sm" class="group/chat hover:bg-muted relative flex-nowrap" [class.bg-muted]="chat.id === chatList.activeId()" animate.enter="slide-in" animate.leave="fade-away">
              <a [routerLink]="['/chats', chat.id]" class="flex min-w-0 flex-1 items-center gap-3 outline-none" [attr.aria-current]="chat.id === chatList.activeId() ? 'page' : null">
                <div hlmItemMedia>
                  <app-chat-avatar [chat]="chat" [px]="48" />
                </div>
                <div hlmItemContent class="min-w-0">
                  <div hlmItemTitle class="max-w-full truncate" [class.font-bold]="unreadOf(chat.id)">{{ titleOf(chat) }}</div>
                  <p hlmItemDescription class="flex min-w-0 gap-1" [class.text-foreground]="unreadOf(chat.id)" [class.font-semibold]="unreadOf(chat.id)">
                    @if (lineOf(chat); as line) {
                      <span class="truncate">{{ line }}</span>
                      <span aria-hidden="true">·</span>
                    }
                    <span class="shrink-0">{{ whenOf(chat) }}</span>
                  </p>
                </div>
              </a>
              <div hlmItemActions class="md:contents">
                @if (unreadOf(chat.id); as count) {
                  <span hlmBadge class="bg-highlight text-highlight-foreground tabular-nums">{{ count }}<span class="sr-only"> unread</span></span>
                }
                <button
                    hlmBtn
                    variant="ghost"
                    size="icon-sm"
                    class="md:bg-muted md:hover:bg-accent md:pointer-events-none md:absolute md:end-3 md:top-1/2 md:-translate-y-1/2 md:opacity-0 md:group-hover/chat:pointer-events-auto md:group-hover/chat:opacity-100 md:focus-visible:pointer-events-auto md:focus-visible:opacity-100 md:aria-expanded:pointer-events-auto md:aria-expanded:opacity-100"
                    [hlmDropdownMenuTrigger]="menu"
                    [hlmDropdownMenuTriggerData]="{ $implicit: { id: chat.id, name: titleOf(chat), avatarUrl: chat.avatarUrl, direct: chat.isDirect, owner: chatList.owns(chat.id) } }"
                    align="end"
                  >
                    <ng-icon name="lucideEllipsis" />
                    <span class="sr-only">Actions for {{ titleOf(chat) }}</span>
                  </button>
              </div>
            </div>
          }
        </div>
      } @empty {
        <hlm-empty>
          <hlm-empty-header>
            <div hlmEmptyMedia variant="icon"><ng-icon name="lucideMessageCircle" /></div>
            <h3 hlmEmptyTitle class="font-display text-xl font-extrabold">{{ filter() ? 'No chat matches' : 'No chats yet' }}</h3>
            <p hlmEmptyDescription>{{ filter() ? 'Try another name.' : 'Start one and bring some bots.' }}</p>
          </hlm-empty-header>
        </hlm-empty>
      }
    }

    <ng-template #menu let-chat>
      <hlm-dropdown-menu class="w-44">
        <!-- The owner edits and deletes; anyone else can only leave. A direct chat has nothing to edit. -->
        @if (chat.owner) {
          @if (!chat.direct) {
            <button hlmDropdownMenuItem (triggered)="editing.set(chat)">
              <ng-icon name="lucidePencil" />
              Edit
            </button>
          }
          <button hlmDropdownMenuItem variant="destructive" (triggered)="deleting.set(chat)">
            <ng-icon name="lucideTrash2" />
            Delete
          </button>
        } @else {
          <button hlmDropdownMenuItem variant="destructive" (triggered)="leaving.set(chat)">
            <ng-icon name="lucideLogOut" />
            Leave chat
          </button>
        }
      </hlm-dropdown-menu>
    </ng-template>

    <app-edit-chat-dialog [chat]="editing()" (closed)="editing.set(null)" />
    <hlm-alert-dialog [state]="leaving() ? 'open' : 'closed'" (closed)="leaving.set(null)">
      <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
        <hlm-alert-dialog-header>
          <h2 hlmAlertDialogTitle>Leave “{{ leaving()?.name }}”?</h2>
          <p hlmAlertDialogDescription>It goes from your chats. Someone in it can add you back.</p>
        </hlm-alert-dialog-header>
        <hlm-alert-dialog-footer>
          <button hlmAlertDialogCancel (click)="ctx.close()">Cancel</button>
          <button hlmAlertDialogAction variant="destructive" (click)="leave(); ctx.close()">Leave</button>
        </hlm-alert-dialog-footer>
      </hlm-alert-dialog-content>
    </hlm-alert-dialog>
    <app-delete-chat-dialog [chat]="deleting()" (closed)="deleting.set(null)" />
  `,
})
export class ChatInbox {
  protected readonly chatList = inject(ChatList);

  /** Narrows the list to chats whose name contains it, ignoring case. */
  readonly filter = input('');

  protected readonly skeletonRows = SKELETON_ROWS;
  protected readonly editing = signal<ChatRef | null>(null);
  protected readonly leaving = signal<ChatRef | null>(null);
  protected readonly deleting = signal<ChatRef | null>(null);

  protected readonly groups = computed(() => {
    const query = this.filter().trim().toLowerCase();
    const chats = query
      ? this.chatList.chats().filter((chat) => this.titleOf(chat).toLowerCase().includes(query))
      : this.chatList.chats();
    return groupChatsByDay(chats, new Date());
  });

  protected unreadOf(chatId: string): number {
    return this.chatList.unread().get(chatId) ?? 0;
  }

  protected titleOf(chat: ChatDto): string {
    return chatTitle(chat);
  }

  /** "Rex: on my way", or how many are in it when nothing was said yet. */
  protected lineOf(chat: ChatDto): string {
    const preview = chat.preview;
    // In a direct chat only your own lines say who wrote them, as on Instagram.
    const sender = chat.isDirect && preview?.lastSenderName !== 'You' ? null : preview?.lastSenderName;
    if (preview?.lastMessage) return `${sender ? sender + ': ' : ''}${preview.lastMessage}`;
    if (preview?.memberCount) return `${preview.memberCount} ${preview.memberCount === 1 ? 'member' : 'members'}`;
    return '';
  }

  /** How long ago the last message came, kept whole when the line is cut. */
  protected whenOf(chat: ChatDto): string {
    return formatAgo(chat.preview?.lastMessageAt ?? chat.updatedAt);
  }

  protected async leave(): Promise<void> {
    const chat = this.leaving();
    if (!chat) return;
    try {
      await this.chatList.leave(chat.id);
    } catch (error) {
      toast.error(describeApiError(error, { fallback: `Could not leave ${chat.name}.` }));
    }
  }
}
