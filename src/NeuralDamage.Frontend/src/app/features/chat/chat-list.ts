import { computed, inject, Service, signal } from '@angular/core';
import { toast } from '@spartan-ng/brain/sonner';
import { firstValueFrom } from 'rxjs';
import { ChatsService } from '../../api/api/chats.service';
import { AuthService } from '../../core/auth/auth.service';
import { ChatDetailDto, ChatDto, MessageDto } from '../../core/models';
import { SignalRService } from '../../core/signalr/signalr.service';

/**
 * The signed-in user's chats, newest activity first, kept live from the hubs. Also counts messages
 * that arrive in chats other than the open one, since the API has no read markers yet.
 */
@Service()
export class ChatList {
  private readonly api = inject(ChatsService);
  private readonly signalr = inject(SignalRService);
  private readonly auth = inject(AuthService);

  private readonly list = signal<ChatDto[]>([]);
  readonly loading = signal(true);
  readonly chats = computed(() =>
    [...this.list()].sort((a, b) => b.updatedAt.localeCompare(a.updatedAt)),
  );
  readonly unread = signal<ReadonlyMap<string, number>>(new Map());

  /** Whether the signed-in user owns `chatId`: only the owner may rename or delete it. */
  owns(chatId: string): boolean {
    const me = this.auth.user()?.id;
    return !!me && this.list().some((chat) => chat.id === chatId && chat.createdById === me);
  }

  /** The chat on screen, whose messages are not unread. */
  readonly activeId = signal<string | null>(null);

  /** Chats this user deleted, so their own deletion does not read as the chat vanishing on them. */
  private readonly deletedHere = new Set<string>();

  private started = false;

  async start(): Promise<void> {
    if (this.started) return;
    this.started = true;
    this.signalr.onUserEvent('ChatCreated', this.upsert);
    this.signalr.onUserEvent('ChatJoined', (chat: ChatDetailDto) => this.upsert(chat));
    this.signalr.onUserEvent('ChatLeft', this.drop);
    this.signalr.onChatEvent('ChatUpdated', this.upsert);
    this.signalr.onChatEvent('ChatDeleted', this.drop);
    this.signalr.onChatEvent('MessageNew', this.onMessage);
    await this.reload();
    void this.signalr.start();
  }

  async reload(): Promise<void> {
    try {
      this.list.set(await firstValueFrom(this.api.apiChatsGet()));
    } catch {
      toast.error('Could not load your chats.');
    } finally {
      this.loading.set(false);
    }
  }

  /**
   * The API accepts the command and announces the chat on the user hub, so the new chat's id comes
   * from that event. Resolves with null if it does not arrive in time; the list still updates.
   */
  async create(name: string): Promise<ChatDto | null> {
    let settle: (chat: ChatDto | null) => void = () => undefined;
    const created = new Promise<ChatDto | null>((resolve) => (settle = resolve));
    const onCreated = (chat: ChatDto) => {
      if (chat.name === name && chat.createdById === this.auth.user()?.id) settle(chat);
    };
    this.signalr.onUserEvent('ChatCreated', onCreated);
    const timer = setTimeout(() => settle(null), 5_000);
    try {
      await firstValueFrom(this.api.apiChatsPost({ name }));
      return await created;
    } finally {
      clearTimeout(timer);
      this.signalr.offUserEvent('ChatCreated', onCreated);
    }
  }

  async rename(id: string, name: string): Promise<void> {
    await firstValueFrom(this.api.apiChatsChatIdPut(id, { name }));
    this.list.update((list) => list.map((c) => (c.id === id ? { ...c, name } : c)));
  }

  async remove(id: string): Promise<void> {
    this.deletedHere.add(id);
    try {
      await firstValueFrom(this.api.apiChatsChatIdDelete(id));
    } catch (error) {
      this.deletedHere.delete(id);
      throw error;
    }
    this.drop(id);
  }

  wasDeletedHere(id: string): boolean {
    return this.deletedHere.has(id);
  }

  markRead(id: string): void {
    this.unread.update((map) => {
      if (!map.has(id)) return map;
      const next = new Map(map);
      next.delete(id);
      return next;
    });
  }

  private readonly upsert = (chat: ChatDto) => {
    const { id, name, createdById, createdAt, updatedAt } = chat;
    const flat: ChatDto = { id, name, createdById, createdAt, updatedAt };
    this.list.update((list) =>
      list.some((c) => c.id === id)
        ? list.map((c) => (c.id === id ? { ...c, ...flat } : c))
        : [flat, ...list],
    );
  };

  private readonly drop = (chatId: string) => {
    this.list.update((list) => list.filter((c) => c.id !== chatId));
    this.markRead(chatId);
  };

  private readonly onMessage = (message: MessageDto) => {
    this.list.update((list) =>
      list.map((c) => (c.id === message.chatId ? { ...c, updatedAt: message.createdAt } : c)),
    );
    const mine = message.senderUserId != null && message.senderUserId === this.auth.user()?.id;
    if (mine || message.chatId === this.activeId()) return;
    this.unread.update((map) => new Map(map).set(message.chatId, (map.get(message.chatId) ?? 0) + 1));
  };
}
