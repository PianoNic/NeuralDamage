import { ChangeDetectionStrategy, Component, computed, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmAvatar, HlmAvatarFallback, HlmAvatarImage } from '@spartan-ng/helm/avatar';
import { HlmInput } from '@spartan-ng/helm/input';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmAlertDialogImports } from '@spartan-ng/helm/alert-dialog';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmSidebarImports } from '@spartan-ng/helm/sidebar';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideBrain,
  lucideChevronsUpDown,
  lucideLogOut,
  lucideMoon,
  lucideSearch,
  lucideSquarePen,
  lucideSun,
} from '@ng-icons/lucide';
import { PkConversationList, type Conversation } from '@prompt-kit/conversation-list';
import { AuthService } from '@app/shared/auth/auth.service';
import { SignalRService } from '@app/shared/signalr/signalr.service';
import { ChatsService } from '@app/api/api/chats.service';
import { Chat } from '@app/models';
import { filter, firstValueFrom, map } from 'rxjs';

@Component({
  selector: 'app-sidebar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    RouterLink,
    HlmButton,
    HlmAvatar,
    HlmAvatarFallback,
    HlmAvatarImage,
    HlmInput,
    HlmDialogImports,
    HlmAlertDialogImports,
    HlmDropdownMenuImports,
    HlmSidebarImports,
    PkConversationList,
    NgIcon,
  ],
  viewProviders: [
    provideIcons({
      lucideBrain,
      lucideChevronsUpDown,
      lucideLogOut,
      lucideMoon,
      lucideSearch,
      lucideSquarePen,
      lucideSun,
    }),
  ],
  templateUrl: './sidebar.html',
})
export class SidebarComponent implements OnInit, OnDestroy {
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);
  private readonly signalr = inject(SignalRService);
  private readonly chatsApi = inject(ChatsService);

  readonly chats = signal<Chat[]>([]);
  readonly searchQuery = signal('');
  readonly user = this.auth.user;
  readonly displayName = this.auth.displayName;
  readonly avatarUrl = this.auth.avatarUrl;

  readonly showCreateDialog = signal(false);
  readonly newChatName = signal('');
  readonly showDeleteDialog = signal(false);
  readonly deletingChatId = signal<string | null>(null);
  readonly isDark = signal(
    localStorage.getItem('theme') === 'dark' ||
    (!localStorage.getItem('theme') && window.matchMedia('(prefers-color-scheme: dark)').matches)
  );

  constructor() {
    document.documentElement.classList.toggle('dark', this.isDark());
  }

  toggleTheme() {
    const dark = !this.isDark();
    this.isDark.set(dark);
    document.documentElement.classList.toggle('dark', dark);
    localStorage.setItem('theme', dark ? 'dark' : 'light');
  }

  readonly filteredChats = computed(() => {
    const query = this.searchQuery().toLowerCase();
    if (!query) return this.chats();
    return this.chats().filter((c) => c.name.toLowerCase().includes(query));
  });

  /** The shape pk-conversation-list expects. */
  readonly conversations = computed<Conversation[]>(() =>
    this.filteredChats().map((c) => ({
      id: c.id,
      title: c.name,
      updatedAt: c.createdAt,
    })),
  );

  private readonly navigationUrl = toSignal(
    this.router.events.pipe(
      filter((e): e is NavigationEnd => e instanceof NavigationEnd),
      map((e) => e.urlAfterRedirects),
    ),
    { initialValue: this.router.url },
  );

  readonly activeChatId = computed(() => {
    const match = /\/chat\/([^/?#]+)/.exec(this.navigationUrl());
    return match ? match[1] : null;
  });

  openChat(chatId: string): void {
    void this.router.navigate(['/chat', chatId]);
  }


  private onChatCreated = (chat: Chat) => {
    this.chats.update((list) => [chat, ...list]);
  };
  private onChatJoined = (chat: any) => {
    this.chats.update((list) => [chat, ...list]);
  };
  private onChatUpdated = (chat: Chat) => {
    this.chats.update((list) => list.map((c) => (c.id === chat.id ? { ...c, ...chat } : c)));
  };
  private onChatDeleted = (chatId: string) => {
    this.chats.update((list) => list.filter((c) => c.id !== chatId));
  };
  private onChatLeft = (chatId: string) => {
    this.chats.update((list) => list.filter((c) => c.id !== chatId));
  };

  async ngOnInit() {
    await this.loadChats();
    await this.signalr.start();
    this.signalr.onUserEvent('ChatCreated', this.onChatCreated);
    this.signalr.onUserEvent('ChatJoined', this.onChatJoined);
    this.signalr.onUserEvent('ChatLeft', this.onChatLeft);
    this.signalr.onChatEvent('ChatUpdated', this.onChatUpdated);
    this.signalr.onChatEvent('ChatDeleted', this.onChatDeleted);
  }

  ngOnDestroy() {
    this.signalr.offUserEvent('ChatCreated', this.onChatCreated);
    this.signalr.offUserEvent('ChatJoined', this.onChatJoined);
    this.signalr.offUserEvent('ChatLeft', this.onChatLeft);
    this.signalr.offChatEvent('ChatUpdated', this.onChatUpdated);
    this.signalr.offChatEvent('ChatDeleted', this.onChatDeleted);
  }

  openCreateDialog() {
    this.newChatName.set('');
    this.showCreateDialog.set(true);
  }

  async submitCreateChat() {
    const name = this.newChatName().trim();
    if (!name) return;
    await firstValueFrom(this.chatsApi.apiChatsPost({ name }));
    this.showCreateDialog.set(false);
  }

  confirmDeleteChat(chatId: string) {
    this.deletingChatId.set(chatId);
    this.showDeleteDialog.set(true);
  }

  async executeDeleteChat() {
    const chatId = this.deletingChatId();
    if (!chatId) return;
    await firstValueFrom(this.chatsApi.apiChatsChatIdDelete(chatId));
    this.showDeleteDialog.set(false);
    this.deletingChatId.set(null);
  }

  logout() {
    this.signalr.stop();
    this.auth.logout();
  }

  private async loadChats() {
    try {
      const chats = (await firstValueFrom(this.chatsApi.apiChatsGet())) as Chat[];
      this.chats.set(chats);
    } catch {
      console.error('Failed to load chats');
    }
  }
}
