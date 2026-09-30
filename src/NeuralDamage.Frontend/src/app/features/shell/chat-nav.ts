import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideEllipsis, lucidePencil, lucideTrash2 } from '@ng-icons/lucide';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmSidebarImports, HlmSidebarService } from '@spartan-ng/helm/sidebar';
import { ChatRef, DeleteChatDialog, RenameChatDialog } from '../chat/chat-dialogs';
import { groupChatsByDay } from '../chat/chat-groups';
import { ChatList } from '../chat/chat-list';

const SKELETON_ROWS = [0, 1, 2, 3, 4];

/** The chats in the sidebar, grouped Today / Earlier, with unread counts and a rename/delete menu. */
@Component({
  selector: 'app-chat-nav',
  imports: [
    RouterLink,
    NgIcon,
    HlmDropdownMenuImports,
    HlmSidebarImports,
    RenameChatDialog,
    DeleteChatDialog,
  ],
  providers: [provideIcons({ lucideEllipsis, lucidePencil, lucideTrash2 })],
  host: { class: 'contents' },
  template: `
    @if (chatList.loading() && !chatList.chats().length) {
      <hlm-sidebar-group>
        <ul hlmSidebarMenu>
          @for (row of skeletonRows; track row) {
            <li hlmSidebarMenuItem><hlm-sidebar-menu-skeleton /></li>
          }
        </ul>
      </hlm-sidebar-group>
    } @else {
      @for (group of groups(); track group.label) {
        <hlm-sidebar-group>
          <div hlmSidebarGroupLabel>{{ group.label }}</div>
          <ul hlmSidebarMenu>
            @for (chat of group.chats; track chat.id) {
              <li hlmSidebarMenuItem animate.enter="slide-in" animate.leave="fade-away">
                <a
                  hlmSidebarMenuButton
                  closeMobileSidebarOnClick
                  [routerLink]="['/chat', chat.id]"
                  [isActive]="chat.id === chatList.activeId()"
                  [attr.aria-current]="chat.id === chatList.activeId() ? 'page' : null"
                  [class.font-medium]="chat.id === chatList.activeId()"
                >
                  <span>{{ chat.name }}</span>
                </a>
                @if (chatList.unread().get(chat.id); as count) {
                  <div hlmSidebarMenuBadge class="group-hover/menu-item:opacity-0 group-has-[[aria-expanded=true]]/menu-item:opacity-0">
                    {{ count }}<span class="sr-only"> unread</span>
                  </div>
                }
                @if (chatList.owns(chat.id)) {
                  <button
                    hlmSidebarMenuAction
                    showOnHover
                    [hlmDropdownMenuTrigger]="menu"
                    [hlmDropdownMenuTriggerData]="{ $implicit: chat }"
                    [side]="sidebar.isMobile() ? 'bottom' : 'right'"
                    align="start"
                  >
                    <ng-icon name="lucideEllipsis" />
                    <span class="sr-only">Actions for {{ chat.name }}</span>
                  </button>
                }
              </li>
            }
          </ul>
        </hlm-sidebar-group>
      } @empty {
        <hlm-sidebar-group>
          <p class="text-muted-foreground px-2 text-sm">No chats yet. Start one above.</p>
        </hlm-sidebar-group>
      }
    }

    <ng-template #menu let-chat>
      <hlm-dropdown-menu class="w-44">
        <button hlmDropdownMenuItem (triggered)="renaming.set(chat)">
          <ng-icon name="lucidePencil" />
          Rename
        </button>
        <button hlmDropdownMenuItem variant="destructive" (triggered)="deleting.set(chat)">
          <ng-icon name="lucideTrash2" />
          Delete
        </button>
      </hlm-dropdown-menu>
    </ng-template>

    <app-rename-chat-dialog [chat]="renaming()" (closed)="renaming.set(null)" />
    <app-delete-chat-dialog [chat]="deleting()" (closed)="deleting.set(null)" />
  `,
})
export class ChatNav {
  protected readonly chatList = inject(ChatList);
  protected readonly sidebar = inject(HlmSidebarService);

  protected readonly skeletonRows = SKELETON_ROWS;
  protected readonly renaming = signal<ChatRef | null>(null);
  protected readonly deleting = signal<ChatRef | null>(null);

  protected readonly groups = computed(() => groupChatsByDay(this.chatList.chats(), new Date()));
}
