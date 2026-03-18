import { ChangeDetectionStrategy, Component, computed, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmSeparator } from '@spartan-ng/helm/separator';
import { HlmAvatar, HlmAvatarFallback } from '@spartan-ng/helm/avatar';
import { AuthService } from '@app/shared/auth/auth.service';
import { SignalRService } from '@app/shared/signalr/signalr.service';
import { ChatsService } from '@app/api/api/chats.service';
import { Chat } from '@app/models';
import { firstValueFrom } from 'rxjs';

@Component({
  selector: 'app-sidebar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterLinkActive, HlmButton, HlmSeparator, HlmAvatar, HlmAvatarFallback],
  templateUrl: './sidebar.html',
})
export class SidebarComponent implements OnInit, OnDestroy {
  private readonly auth = inject(AuthService);
  private readonly signalr = inject(SignalRService);
  private readonly chatsApi = inject(ChatsService);

  readonly chats = signal<Chat[]>([]);
  readonly searchQuery = signal('');
  readonly user = this.auth.user;
  readonly displayName = this.auth.displayName;
  readonly avatarUrl = this.auth.avatarUrl;

  readonly filteredChats = computed(() => {
    const query = this.searchQuery().toLowerCase();
    if (!query) return this.chats();
    return this.chats().filter((c) => c.name.toLowerCase().includes(query));
  });

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

  async createChat() {
    const name = prompt('Chat name:');
    if (!name?.trim()) return;
    await firstValueFrom(this.chatsApi.apiChatsPost({ name: name.trim() }));
  }

  async deleteChat(chatId: string) {
    if (!confirm('Delete this chat?')) return;
    await firstValueFrom(this.chatsApi.apiChatsChatIdDelete(chatId));
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
