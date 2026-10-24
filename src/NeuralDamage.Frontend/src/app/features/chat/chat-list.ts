import { computed, inject, Service, signal } from '@angular/core';
import { toast } from '@spartan-ng/brain/sonner';
import { firstValueFrom } from 'rxjs';
import { ChatMembersService } from '../../api/api/chatMembers.service';
import { ChatsService } from '../../api/api/chats.service';
import { MessagesService } from '../../api/api/messages.service';
import { AuthService } from '../../core/auth/auth.service';
import { ChatDetailDto, ChatDto, MessageDto } from '../../core/models';
import { Reconnected, SignalRService } from '../../core/signalr/signalr.service';

/** Slack for the browser's clock against the server's when asking what arrived during an outage. */
const CLOCK_SLACK_MS = 60_000;

/** Message ids remembered for de-duplicating the unread count; old ones fall out first. */
const SEEN_LIMIT = 2_000;

/**
 * The signed-in user's chats, newest activity first, kept live from the hubs. Also counts messages
 * that arrive in chats other than the open one, since the API has no read markers yet.
 */
@Service()
export class ChatList {
  private readonly api = inject(ChatsService);
  private readonly membersApi = inject(ChatMembersService);
  private readonly messagesApi = inject(MessagesService);
  private readonly signalr = inject(SignalRService);
  private readonly auth = inject(AuthService);

  private readonly list = signal<ChatDto[]>([]);
  readonly loading = signal(true);
  readonly chats = computed(() =>
    [...this.list()].sort((a, b) => b.updatedAt.localeCompare(a.updatedAt)),
  );
  readonly unread = signal<ReadonlyMap<string, number>>(new Map());

  /** Whether the signed-in user owns `chatId`, or is an admin: only they may rename or delete it. */
  owns(chatId: string): boolean {
    const me = this.auth.user()?.id;
    if (!me) return false;
    return this.list().some((chat) => chat.id === chatId && (chat.createdById === me || this.auth.isAdmin()));
  }

  /** The chat on screen, whose messages are not unread. */
  readonly activeId = signal<string | null>(null);

  /** Chats this user deleted, so their own deletion does not read as the chat vanishing on them. */
  private readonly deletedHere = new Set<string>();

  /** Messages already counted, so the catch-up after a reconnect does not count them twice. */
  private readonly seen = new Set<string>();

  private started = false;
  private refreshTimer: ReturnType<typeof setTimeout> | undefined;

  /** Several members added at once, as a new chat fills up, cost one reload. */
  private readonly refreshSoon = () => {
    clearTimeout(this.refreshTimer);
    this.refreshTimer = setTimeout(() => void this.reload(), 400);
  };

  async start(): Promise<void> {
    if (this.started) return;
    this.started = true;
    this.signalr.onUserEvent('ChatCreated', this.upsert);
    this.signalr.onUserEvent('ChatJoined', (chat: ChatDetailDto) => this.upsert(chat));
    this.signalr.onUserEvent('ChatLeft', this.drop);
    this.signalr.onChatEvent('ChatUpdated', this.upsert);
    this.signalr.onChatEvent('ChatDeleted', this.drop);
    this.signalr.onChatEvent('MessageNew', this.onMessage);
    // The inbox's faces and head count come with the list, so a change of members fetches it again.
    this.signalr.onChatEvent('MemberAdded', this.refreshSoon);
    this.signalr.onChatEvent('MemberRemoved', this.refreshSoon);
    this.signalr.reconnected$.subscribe((event) => void this.catchUp(event));
    await this.reload();
    void this.signalr.start();
  }

  async reload(): Promise<void> {
    try {
      const fresh = await firstValueFrom(this.api.apiChatsGet());
      // The server only moves updatedAt on a rename; the activity seen here is newer.
      this.list.update((list) => {
        const known = new Map(list.map((c) => [c.id, c.updatedAt]));
        return fresh.map((chat) => ({ ...chat, updatedAt: later(chat.updatedAt, known.get(chat.id)) }));
      });
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

  /** The direct chat with a bot or a person: the one there is, or a new one. */
  async openDirect(to: { botId?: string; userId?: string }): Promise<ChatDto> {
    const chat = await firstValueFrom(this.api.apiChatsDirectPost({ botId: to.botId ?? null, userId: to.userId ?? null }));
    if (!this.list().some((c) => c.id === chat.id)) {
      this.upsert(chat);
      // The list's preview carries who it is with, for its name and picture.
      void this.reload();
    }
    return chat;
  }

  /** A group chat's own picture, already cropped square. Everyone in it hears of it from the hub. */
  async setAvatar(id: string, picture: Blob): Promise<void> {
    const { avatarUrl } = await firstValueFrom(this.api.apiChatsChatIdAvatarPut(id, picture));
    this.list.update((list) => list.map((c) => (c.id === id ? { ...c, avatarUrl } : c)));
  }

  async removeAvatar(id: string): Promise<void> {
    await firstValueFrom(this.api.apiChatsChatIdAvatarDelete(id));
    this.list.update((list) => list.map((c) => (c.id === id ? { ...c, avatarUrl: null } : c)));
  }

  /** Takes you out of a chat someone else runs; the hub's ChatLeft drops it from the list too. */
  async leave(id: string): Promise<void> {
    const me = this.auth.user()?.id;
    const chat = await firstValueFrom(this.api.apiChatsChatIdGet(id));
    const mine = chat.members.find((m) => m.userId === me);
    if (!mine) return;
    await firstValueFrom(this.membersApi.apiChatsChatIdMembersMemberIdDelete(id, mine.id));
    this.list.update((list) => list.filter((c) => c.id !== id));
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
    const { id, name, createdById, createdAt, updatedAt, isDirect, avatarUrl } = chat;
    const flat: ChatDto = { id, name, createdById, createdAt, updatedAt, isDirect, avatarUrl };
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

  /**
   * After an outage: chats created, joined or left meanwhile come with a fresh list, and messages
   * that arrived in each chat since the outage began move it up and count as unread.
   */
  private async catchUp({ since }: Reconnected): Promise<void> {
    await this.reload();
    const after = new Date(since - CLOCK_SLACK_MS).toISOString();
    await Promise.all(
      this.list().map(async (chat) => {
        try {
          const missed = await firstValueFrom(
            this.messagesApi.apiChatsChatIdMessagesGet(chat.id, 100, undefined, after),
          );
          missed.forEach(this.onMessage);
        } catch {
          // The next live message or reload brings this chat up to date.
        }
      }),
    );
  }

  private readonly onMessage = (message: MessageDto) => {
    if (this.seen.has(message.id)) return;
    this.seen.add(message.id);
    if (this.seen.size > SEEN_LIMIT) this.seen.delete(this.seen.values().next().value!);
    const mine = message.senderUserId != null && message.senderUserId === this.auth.user()?.id;
    this.list.update((list) =>
      list.map((c) =>
        c.id === message.chatId
          ? {
              ...c,
              updatedAt: later(c.updatedAt, message.createdAt),
              preview: {
                memberCount: c.preview?.memberCount ?? 0,
                members: c.preview?.members ?? [],
                lastMessage: message.content || (message.sticker ? 'Sent a sticker' : message.attachments?.length ? 'Sent a picture' : ''),
                lastSenderName: mine ? 'You' : (message.senderBot?.name ?? message.senderUser?.displayName ?? null),
                lastMessageAt: message.createdAt,
              },
            }
          : c,
      ),
    );
    if (mine || message.chatId === this.activeId()) return;
    this.unread.update((map) => new Map(map).set(message.chatId, (map.get(message.chatId) ?? 0) + 1));
  };
}

function later(a: string, b: string | undefined): string {
  return b && Date.parse(b) > Date.parse(a) ? b : a;
}
