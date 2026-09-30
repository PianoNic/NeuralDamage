import { HttpErrorResponse } from '@angular/common/http';
import {
  Component,
  computed,
  effect,
  inject,
  input,
  OnDestroy,
  signal,
  untracked,
  viewChild,
  WritableSignal,
} from '@angular/core';
import { Title } from '@angular/platform-browser';
import { Router } from '@angular/router';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { firstValueFrom } from 'rxjs';
import { ChatsService } from '../../api/api/chats.service';
import { MessagesService } from '../../api/api/messages.service';
import { ReactionsService } from '../../api/api/reactions.service';
import { AuthService } from '../../core/auth/auth.service';
import { toastApiError } from '../../core/http-errors';
import {
  ChatDetailDto,
  ChatDto,
  ChatMember,
  ChatMemberDto,
  Message,
  MessageDto,
  ReactionGroupDto,
} from '../../core/models';
import { SystemMessage } from '../../core/signalr/hub-events';
import { SignalRService } from '../../core/signalr/signalr.service';
import { PeopleSheet } from '../people/people-sheet';
import { ChatHeader } from './chat-header';
import { ChatList } from './chat-list';
import { Composer } from './composer';
import {
  toChatMember,
  toChatMembers,
  toMessage,
  toMessages,
  withDescription,
} from './message.mapper';
import { MessageList } from './message-list';
import { Typer, TypingRow } from './typing-row';

/** Messages per request, both for the first page and for each older page. */
const PAGE_SIZE = 50;

/**
 * The backend re-sends BotTyping every ~3s while a bot works, and clients send StartTyping at most
 * every ~3s, so an entry that has not been refreshed within this window has stopped.
 */
const TYPING_TTL_MS = 4000;

/** How often this client tells the others it is typing. */
const TYPING_SEND_INTERVAL_MS = 3000;

/** A chat: header, messages, who is typing, and the composer. Serves `/chat/:chatId`. */
@Component({
  selector: 'app-chat',
  imports: [HlmSpinner, ChatHeader, Composer, MessageList, PeopleSheet, TypingRow],
  host: { class: 'flex h-full min-h-0 flex-col' },
  template: `
    <app-chat-header
      [chatId]="chatId()"
      [name]="chat()?.name ?? ''"
      [members]="members()"
      (people)="peopleOpen.set(true)"
    />

    @if (loading()) {
      <div class="flex flex-1 items-center justify-center"><hlm-spinner /></div>
    } @else {
      <app-message-list
        [messages]="messages()"
        [systemMessages]="systemMessages()"
        [currentUserId]="currentUserId()"
        [memberNames]="memberNames()"
        [hasMore]="hasMore()"
        [loadingOlder]="loadingOlder()"
        (loadOlder)="loadOlder()"
        (replyTo)="replyingTo.set($event)"
        (react)="toggleReaction($event.messageId, $event.emoji)"
        (filesDropped)="composer()?.addImages($event)"
      />
    }

    <div class="mx-auto w-full max-w-3xl shrink-0 px-4 pb-3 md:pb-4">
      <app-typing-row [typers]="typers()" class="min-h-8 ps-1" />
      <app-composer
        [members]="members()"
        [currentUserId]="currentUserId()"
        [replyingTo]="replyingTo()"
        [placeholder]="placeholder()"
        [chatId]="chatId()"
        (send)="sendMessage($event.content, $event.attachmentIds)"
        (typing)="onTyping()"
        (cancelReply)="replyingTo.set(null)"
      />
    </div>

    <app-people-sheet
      [open]="peopleOpen()"
      [chatId]="chatId()"
      [members]="members()"
      [currentUserId]="currentUserId()"
      (closed)="peopleOpen.set(false)"
      (mention)="mention($event)"
      (patchMember)="patchMember($event.id, $event.changes)"
      (refresh)="reloadMembers()"
    />
  `,
})
export class Chat implements OnDestroy {
  private readonly router = inject(Router);
  private readonly title = inject(Title);
  private readonly auth = inject(AuthService);
  private readonly signalr = inject(SignalRService);
  private readonly chatList = inject(ChatList);
  private readonly chatsApi = inject(ChatsService);
  private readonly messagesApi = inject(MessagesService);
  private readonly reactionsApi = inject(ReactionsService);

  /** Route parameter of `/chat/:chatId`. */
  readonly chatId = input.required<string>();

  private readonly list = viewChild(MessageList);
  private readonly composer = viewChild(Composer);

  protected readonly chat = signal<ChatDetailDto | null>(null);
  protected readonly messages = signal<Message[]>([]);
  protected readonly systemMessages = signal<SystemMessage[]>([]);
  protected readonly members = signal<ChatMember[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadingOlder = signal(false);
  protected readonly hasMore = signal(false);
  protected readonly replyingTo = signal<Message | null>(null);
  protected readonly peopleOpen = signal(false);

  /** id → display name; ids are GUIDs, so users and bots share one timer map. */
  private readonly typingUsers = signal<ReadonlyMap<string, string>>(new Map());
  private readonly typingBots = signal<ReadonlyMap<string, string>>(new Map());
  private readonly typingTimers = new Map<string, ReturnType<typeof setTimeout>>();
  private lastTypingSent = 0;

  protected readonly currentUserId = computed(() => this.auth.user()?.id ?? null);
  protected readonly memberNames = computed(() => this.members().map((m) => m.displayName));
  protected readonly placeholder = computed(() => `Message ${this.chat()?.name ?? ''}`.trim());

  protected readonly typers = computed<Typer[]>(() => {
    const members = this.members();
    const find = (id: string) => members.find((m) => m.botId === id || m.userId === id);
    return [...this.typingBots(), ...this.typingUsers()].map(([id, name]) => ({
      id,
      name,
      avatarUrl: find(id)?.avatarUrl ?? null,
    }));
  });

  private currentChatId: string | null = null;

  // The chat hub puts a connection in every chat group the user belongs to, so events for other
  // chats arrive here too and have to be filtered out.

  private readonly onMessageNew = (dto: MessageDto) => {
    if (dto.chatId !== this.currentChatId) return;
    this.clearTyping(dto.senderBotId ?? dto.senderUserId);
    this.messages.update((list) =>
      list.some((m) => m.id === dto.id) ? list : [...list, toMessage(dto)],
    );
  };

  private readonly onMemberAdded = (dto: ChatMemberDto) => {
    if (dto.chatId !== this.currentChatId) return;
    const member = toChatMember(dto);
    this.members.update((list) => (list.some((m) => m.id === member.id) ? list : [...list, member]));
  };

  private readonly onMemberRemoved = (chatId: string, memberId: string) => {
    if (chatId !== this.currentChatId) return;
    this.members.update((list) => list.filter((m) => m.id !== memberId));
  };

  private readonly onBotTyping = (chatId: string, botId: string, botName: string) => {
    if (chatId !== this.currentChatId) return;
    this.markTyping(this.typingBots, botId, botName);
  };

  private readonly onUserTyping = (chatId: string, userId: string, displayName: string) => {
    if (chatId !== this.currentChatId || userId === this.currentUserId()) return;
    this.markTyping(this.typingUsers, userId, displayName);
  };

  private readonly onBotResponseCancelled = (chatId: string) => {
    if (chatId !== this.currentChatId) return;
    for (const botId of this.typingBots().keys()) this.clearTyping(botId);
  };

  private readonly onReactionUpdated = (messageId: string, reactions: ReactionGroupDto[]) => {
    this.messages.update((list) =>
      list.map((m) => (m.id === messageId ? { ...m, reactions: reactions ?? [] } : m)),
    );
  };

  private readonly onAttachmentDescribed = (
    chatId: string,
    messageId: string,
    attachmentId: string,
    description: string,
  ) => {
    if (chatId !== this.currentChatId) return;
    this.messages.update((list) => withDescription(list, messageId, attachmentId, description));
  };

  private readonly onChatCleared = (chatId: string) => {
    if (chatId !== this.currentChatId) return;
    this.messages.set([]);
    this.hasMore.set(false);
  };

  private readonly onChatUpdated = (chat: ChatDto) => {
    if (chat.id !== this.currentChatId) return;
    this.chat.update((current) => (current ? { ...current, ...chat } : current));
  };

  private readonly onChatGone = (chatId: string) => {
    if (chatId !== this.currentChatId) return;
    // Whoever deleted it is already on their way home; everyone else is told why it vanished.
    if (!this.chatList.wasDeletedHere(chatId)) toast.info('This chat is no longer available.');
    this.currentChatId = null;
    void this.router.navigate(['/']);
  };

  private readonly onSystemMessage = (message: SystemMessage) => {
    if (message.chatId !== this.currentChatId) return;
    this.systemMessages.update((list) => [...list, message]);
    // Notices follow member state changes the hub has no event for (a mute, a model gone bad).
    void this.reloadMembers();
  };

  constructor() {
    // Angular reuses this component when only the id changes, so a chat switch reloads here.
    effect(() => {
      const chatId = this.chatId();
      untracked(() => {
        if (chatId !== this.currentChatId) void this.loadChat(chatId);
      });
    });

    effect(() => {
      const name = this.chat()?.name;
      this.title.setTitle(name ? `${name} · Neural Damage` : 'Neural Damage');
    });
  }

  ngOnDestroy(): void {
    if (this.currentChatId) {
      void this.signalr.leaveChat(this.currentChatId).catch(() => undefined);
    }
    this.chatList.activeId.set(null);
    this.unsubscribeEvents();
    this.resetTyping();
  }

  protected async sendMessage(content: string, attachmentIds: string[] = []): Promise<void> {
    if (!this.currentChatId) return;
    const replyToId = this.replyingTo()?.id;
    this.list()?.scrollToBottom();
    try {
      await firstValueFrom(
        this.messagesApi.apiChatsChatIdMessagesPost(this.currentChatId, {
          content,
          replyToId,
          attachmentIds: attachmentIds.length ? attachmentIds : undefined,
        }),
      );
      this.replyingTo.set(null);
      // Let the next keystroke announce typing again straight away.
      this.lastTypingSent = 0;
    } catch (error) {
      this.composer()?.restoreDraft(content);
      const rejected = error instanceof HttpErrorResponse && error.status === 400;
      toastApiError(
        error,
        rejected ? undefined : { network: 'Message not sent. Check your connection and try again.' },
      );
    }
  }

  protected mention(name: string): void {
    this.peopleOpen.set(false);
    this.composer()?.insertMention(name);
  }

  protected patchMember(id: string, changes: Partial<ChatMember>): void {
    this.members.update((list) => list.map((m) => (m.id === id ? { ...m, ...changes } : m)));
  }

  /** Re-reads the member list (mute state, model status); the rest of the chat stays as it is. */
  protected async reloadMembers(): Promise<void> {
    const chatId = this.currentChatId;
    if (!chatId) return;
    try {
      const chat = await firstValueFrom(this.chatsApi.apiChatsChatIdGet(chatId));
      if (chatId === this.currentChatId) this.members.set(toChatMembers(chat.members));
    } catch {
      // Keep what is shown; the next notice or reload tries again.
    }
  }

  protected onTyping(): void {
    const now = Date.now();
    if (!this.currentChatId || now - this.lastTypingSent < TYPING_SEND_INTERVAL_MS) return;
    this.lastTypingSent = now;
    this.signalr.startTyping(this.currentChatId);
  }

  protected async loadOlder(): Promise<void> {
    const chatId = this.currentChatId;
    const oldest = this.messages()[0];
    if (!chatId || !oldest || !this.hasMore() || this.loadingOlder()) return;

    this.loadingOlder.set(true);
    try {
      // The API pages by timestamp: everything strictly older than `before`.
      const dtos = await firstValueFrom(
        this.messagesApi.apiChatsChatIdMessagesGet(chatId, PAGE_SIZE, oldest.createdAt),
      );
      if (chatId !== this.currentChatId) return;
      this.messages.update((list) => {
        const known = new Set(list.map((m) => m.id));
        return [...toMessages(dtos).filter((m) => !known.has(m.id)), ...list];
      });
      this.hasMore.set(dtos.length === PAGE_SIZE);
    } catch {
      toast.error('Could not load older messages.');
    } finally {
      this.loadingOlder.set(false);
    }
  }

  protected async toggleReaction(messageId: string, emoji: string): Promise<void> {
    if (!this.currentChatId) return;
    try {
      await firstValueFrom(
        this.reactionsApi.apiChatsChatIdMessagesMessageIdReactionsEmojiPost(
          this.currentChatId,
          messageId,
          emoji,
        ),
      );
    } catch (error) {
      toastApiError(error, { fallback: 'Could not update the reaction.' });
    }
  }

  private markTyping(target: WritableSignal<ReadonlyMap<string, string>>, id: string, name: string) {
    target.update((map) => new Map(map).set(id, name));
    clearTimeout(this.typingTimers.get(id));
    this.typingTimers.set(id, setTimeout(() => this.clearTyping(id), TYPING_TTL_MS));
  }

  private clearTyping(id: string | null | undefined) {
    if (!id) return;
    clearTimeout(this.typingTimers.get(id));
    this.typingTimers.delete(id);
    for (const target of [this.typingBots, this.typingUsers]) {
      target.update((map) => {
        if (!map.has(id)) return map;
        const next = new Map(map);
        next.delete(id);
        return next;
      });
    }
  }

  private resetTyping() {
    this.typingTimers.forEach((timer) => clearTimeout(timer));
    this.typingTimers.clear();
    this.typingBots.set(new Map());
    this.typingUsers.set(new Map());
  }

  private async loadChat(chatId: string) {
    this.unsubscribeEvents();

    const previousChatId = this.currentChatId;
    if (previousChatId) void this.signalr.leaveChat(previousChatId).catch(() => undefined);

    this.currentChatId = chatId;
    this.chatList.activeId.set(chatId);
    this.chatList.markRead(chatId);
    this.loading.set(true);
    this.resetTyping();
    this.systemMessages.set([]);
    this.replyingTo.set(null);
    this.hasMore.set(false);
    this.peopleOpen.set(false);

    try {
      const chat = await firstValueFrom(this.chatsApi.apiChatsChatIdGet(chatId));
      if (chatId !== this.currentChatId) return;
      this.chat.set(chat);
      this.members.set(toChatMembers(chat.members));

      const dtos = await firstValueFrom(this.messagesApi.apiChatsChatIdMessagesGet(chatId, PAGE_SIZE));
      if (chatId !== this.currentChatId) return;
      this.messages.set(toMessages(dtos));
      this.hasMore.set(dtos.length === PAGE_SIZE);

      // Subscribing first means nothing sent between the join and the subscription is lost.
      // Joining puts this client in the chat's group for chats created after it connected.
      this.subscribeEvents();
      await this.signalr.joinChat(chatId);
    } catch {
      toast.error('Could not open this chat. Try reloading the page.');
    }

    this.loading.set(false);
  }

  private subscribeEvents() {
    this.signalr.onChatEvent('MessageNew', this.onMessageNew);
    this.signalr.onChatEvent('MemberAdded', this.onMemberAdded);
    this.signalr.onChatEvent('MemberRemoved', this.onMemberRemoved);
    this.signalr.onChatEvent('BotTyping', this.onBotTyping);
    this.signalr.onChatEvent('UserTyping', this.onUserTyping);
    this.signalr.onChatEvent('BotResponseCancelled', this.onBotResponseCancelled);
    this.signalr.onChatEvent('ReactionUpdated', this.onReactionUpdated);
    this.signalr.onChatEvent('AttachmentDescribed', this.onAttachmentDescribed);
    this.signalr.onChatEvent('ChatCleared', this.onChatCleared);
    this.signalr.onChatEvent('ChatUpdated', this.onChatUpdated);
    this.signalr.onChatEvent('ChatDeleted', this.onChatGone);
    this.signalr.onChatEvent('SystemMessage', this.onSystemMessage);
    this.signalr.onUserEvent('ChatLeft', this.onChatGone);
  }

  private unsubscribeEvents() {
    this.signalr.offChatEvent('MessageNew', this.onMessageNew);
    this.signalr.offChatEvent('MemberAdded', this.onMemberAdded);
    this.signalr.offChatEvent('MemberRemoved', this.onMemberRemoved);
    this.signalr.offChatEvent('BotTyping', this.onBotTyping);
    this.signalr.offChatEvent('UserTyping', this.onUserTyping);
    this.signalr.offChatEvent('BotResponseCancelled', this.onBotResponseCancelled);
    this.signalr.offChatEvent('ReactionUpdated', this.onReactionUpdated);
    this.signalr.offChatEvent('AttachmentDescribed', this.onAttachmentDescribed);
    this.signalr.offChatEvent('ChatCleared', this.onChatCleared);
    this.signalr.offChatEvent('ChatUpdated', this.onChatUpdated);
    this.signalr.offChatEvent('ChatDeleted', this.onChatGone);
    this.signalr.offChatEvent('SystemMessage', this.onSystemMessage);
    this.signalr.offUserEvent('ChatLeft', this.onChatGone);
  }
}
