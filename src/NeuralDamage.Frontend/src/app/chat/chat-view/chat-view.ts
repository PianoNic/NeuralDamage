import { toast } from '@spartan-ng/brain/sonner';
import { ChangeDetectionStrategy, Component, computed, effect, inject, OnDestroy, signal, WritableSignal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmSidebarTrigger } from '@spartan-ng/helm/sidebar';
import { PkLoader } from '@prompt-kit/loader';
import { HlmAvatar, HlmAvatarFallback, HlmAvatarImage } from '@spartan-ng/helm/avatar';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideBot, lucideUsers } from '@ng-icons/lucide';
import { ChatDetailDto, ChatDto, ChatMember, ChatMemberDto, Message, MessageDto, ReactionGroupDto } from '@app/models';
import { AuthService } from '@app/shared/auth/auth.service';
import { SignalRService } from '@app/shared/signalr/signalr.service';
import { SystemMessage } from '@app/shared/signalr/hub-events';
import { ChatsService } from '@app/api/api/chats.service';
import { MessagesService } from '@app/api/api/messages.service';
import { ReactionsService } from '@app/api/api/reactions.service';
import { toChatMember, toChatMembers, toMessage, toMessages } from '@app/chat/message.mapper';
import { MessageListComponent } from '@app/chat/message-list/message-list';
import { MessageInputComponent } from '@app/chat/message-input/message-input';
import { TypingIndicatorComponent } from '@app/chat/typing-indicator/typing-indicator';
import { BotManagerComponent } from '@app/bots/bot-manager/bot-manager';
import { firstValueFrom, map } from 'rxjs';

/** Messages per request, both for the first page and for each older page. */
const PAGE_SIZE = 50;

/**
 * The backend re-sends BotTyping every ~3s while a bot works, and clients send
 * StartTyping at most every ~3s, so an entry that has not been refreshed within
 * this window has stopped.
 */
const TYPING_TTL_MS = 4000;

/** How often this client tells the others it is typing. */
const TYPING_SEND_INTERVAL_MS = 3000;

@Component({
  selector: 'app-chat-view',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    HlmButton,
    HlmAvatar,
    HlmAvatarFallback,
    HlmAvatarImage,
    HlmSidebarTrigger,
    PkLoader,
    NgIcon,
    MessageListComponent,
    MessageInputComponent,
    TypingIndicatorComponent,
    BotManagerComponent,
  ],
  viewProviders: [provideIcons({ lucideBot, lucideUsers })],
  templateUrl: './chat-view.html',
  host: { class: 'flex min-h-0 flex-1' },
})
export class ChatViewComponent implements OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);
  private readonly signalr = inject(SignalRService);
  private readonly chatsApi = inject(ChatsService);
  private readonly messagesApi = inject(MessagesService);
  private readonly reactionsApi = inject(ReactionsService);

  readonly chat = signal<ChatDetailDto | null>(null);
  readonly messages = signal<Message[]>([]);
  readonly systemMessages = signal<SystemMessage[]>([]);
  readonly members = signal<ChatMember[]>([]);
  readonly loading = signal(true);
  readonly loadingOlder = signal(false);
  readonly hasMore = signal(false);
  readonly botManagerOpen = signal(false);

  /** id → display name; ids are GUIDs, so users and bots share one timer map. */
  readonly typingUsers = signal<Map<string, string>>(new Map());
  readonly typingBots = signal<Map<string, string>>(new Map());
  private readonly typingTimers = new Map<string, ReturnType<typeof setTimeout>>();
  private lastTypingSent = 0;

  readonly currentUserId = computed(() => this.auth.user()?.id ?? null);
  readonly chatName = computed(() => this.chat()?.name ?? '');
  readonly memberCount = computed(() => this.members().length);
  readonly userCount = computed(() => this.members().filter((m) => m.memberType === 'user').length);
  readonly botCount = computed(() => this.members().filter((m) => m.memberType === 'bot').length);
  readonly botMembers = computed(() => this.members().filter((m) => m.memberType === 'bot'));
  readonly replyingTo = signal<Message | null>(null);

  currentChatId: string | null = null;

  // The chat hub puts a connection in every chat group the user belongs to,
  // so events for other chats arrive here too and have to be filtered out.

  private onMessageNew = (dto: MessageDto) => {
    if (dto.chatId !== this.currentChatId) return;
    this.clearTyping(dto.senderBotId ?? dto.senderUserId);
    this.messages.update((list) => (list.some((m) => m.id === dto.id) ? list : [...list, toMessage(dto)]));
  };

  private onMemberAdded = (dto: ChatMemberDto) => {
    if (dto.chatId !== this.currentChatId) return;
    const member = toChatMember(dto);
    this.members.update((list) =>
      list.some((m) => m.id === member.id) ? list : [...list, member],
    );
  };

  private onMemberRemoved = (chatId: string, memberId: string) => {
    if (chatId !== this.currentChatId) return;
    this.members.update((list) => list.filter((m) => m.id !== memberId));
  };

  private onBotTyping = (chatId: string, botId: string, botName: string) => {
    if (chatId !== this.currentChatId) return;
    this.markTyping(this.typingBots, botId, botName);
  };

  private onUserTyping = (chatId: string, userId: string, displayName: string) => {
    if (chatId !== this.currentChatId || userId === this.currentUserId()) return;
    this.markTyping(this.typingUsers, userId, displayName);
  };

  private onBotResponseCancelled = (chatId: string) => {
    if (chatId !== this.currentChatId) return;
    for (const botId of this.typingBots().keys()) this.clearTyping(botId);
  };

  private onReactionUpdated = (messageId: string, reactions: ReactionGroupDto[]) => {
    this.messages.update((list) =>
      list.map((m) => (m.id === messageId ? { ...m, reactions: reactions ?? [] } : m)),
    );
  };

  private onChatCleared = (chatId: string) => {
    if (chatId !== this.currentChatId) return;
    this.messages.set([]);
    this.hasMore.set(false);
  };

  private onChatUpdated = (chat: ChatDto) => {
    if (chat.id !== this.currentChatId) return;
    this.chat.update((current) => (current ? { ...current, ...chat } : current));
  };

  private onChatGone = (chatId: string) => {
    if (chatId !== this.currentChatId) return;
    toast.info('This chat is no longer available.');
    this.currentChatId = null;
    void this.router.navigate(['/']);
  };

  private onSystemMessage = (message: SystemMessage) => {
    if (message.chatId !== this.currentChatId) return;
    this.systemMessages.update((list) => [...list, message]);
  };

  /**
   * route.snapshot is a plain object, so an effect reading it registers no
   * dependency and fires only once. Angular reuses this component when only the
   * chatId changes, so switching chats never reloaded. Track the param map,
   * which is an observable, instead.
   */
  private readonly routeChatId = toSignal(this.route.paramMap.pipe(map((p) => p.get('chatId'))), {
    initialValue: this.route.snapshot.paramMap.get('chatId'),
  });

  constructor() {
    effect(() => {
      const chatId = this.routeChatId();
      if (chatId && chatId !== this.currentChatId) {
        this.loadChat(chatId);
      }
    });
  }

  ngOnDestroy() {
    if (this.currentChatId) {
      void this.signalr.leaveChat(this.currentChatId).catch(() => undefined);
    }
    this.unsubscribeEvents();
    this.resetTyping();
  }

  async sendMessage(event: { content: string; mentions: string[] }) {
    if (!this.currentChatId) return;
    const replyToId = this.replyingTo()?.id;
    try {
      await firstValueFrom(
        this.messagesApi.apiChatsChatIdMessagesPost(this.currentChatId, { content: event.content, replyToId }),
      );
      this.replyingTo.set(null);
      // Let the next keystroke announce typing again straight away.
      this.lastTypingSent = 0;
    } catch {
      toast.error('Message not sent. Check your connection and try again.');
    }
  }

  onTyping() {
    const now = Date.now();
    if (!this.currentChatId || now - this.lastTypingSent < TYPING_SEND_INTERVAL_MS) return;
    this.lastTypingSent = now;
    this.signalr.startTyping(this.currentChatId);
  }

  async loadOlder() {
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

  async toggleReaction(messageId: string, emoji: string) {
    if (!this.currentChatId) return;
    try {
      await firstValueFrom(
        this.reactionsApi.apiChatsChatIdMessagesMessageIdReactionsEmojiPost(this.currentChatId, messageId, emoji),
      );
    } catch {
      toast.error('Could not update the reaction.');
    }
  }

  toggleBotManager() {
    this.botManagerOpen.update((v) => !v);
  }

  setReplyTo(message: Message | null) {
    this.replyingTo.set(message);
  }

  private markTyping(target: WritableSignal<Map<string, string>>, id: string, name: string) {
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
    if (previousChatId) {
      void this.signalr.leaveChat(previousChatId).catch(() => undefined);
    }

    this.currentChatId = chatId;
    this.loading.set(true);
    this.resetTyping();
    this.systemMessages.set([]);
    this.replyingTo.set(null);
    this.hasMore.set(false);

    try {
      const chat = await firstValueFrom(this.chatsApi.apiChatsChatIdGet(chatId));
      if (chatId !== this.currentChatId) return;
      this.chat.set(chat);
      this.members.set(toChatMembers(chat.members));

      const dtos = await firstValueFrom(this.messagesApi.apiChatsChatIdMessagesGet(chatId, PAGE_SIZE));
      if (chatId !== this.currentChatId) return;
      this.messages.set(toMessages(dtos));
      this.hasMore.set(dtos.length === PAGE_SIZE);

      // Must come after start(); joining is what puts this client in the
      // chat's SignalR group for chats created after the connection opened.
      await this.signalr.joinChat(chatId);
      this.subscribeEvents();
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
    this.signalr.offChatEvent('ChatCleared', this.onChatCleared);
    this.signalr.offChatEvent('ChatUpdated', this.onChatUpdated);
    this.signalr.offChatEvent('ChatDeleted', this.onChatGone);
    this.signalr.offChatEvent('SystemMessage', this.onSystemMessage);
    this.signalr.offUserEvent('ChatLeft', this.onChatGone);
  }
}
