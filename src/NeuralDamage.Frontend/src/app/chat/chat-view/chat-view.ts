import { toast } from '@spartan-ng/brain/sonner';
import { ChangeDetectionStrategy, Component, computed, effect, inject, OnDestroy, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmSidebarTrigger } from '@spartan-ng/helm/sidebar';
import { PkLoader } from '@prompt-kit/loader';
import { HlmAvatar, HlmAvatarFallback, HlmAvatarImage } from '@spartan-ng/helm/avatar';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideBot, lucideUsers } from '@ng-icons/lucide';
import { ChatDetailDto, ChatMember, ChatMemberDto, Message, MessageDto, ReactionGroupDto } from '@app/models';
import { AuthService } from '@app/shared/auth/auth.service';
import { SignalRService } from '@app/shared/signalr/signalr.service';
import { ChatsService } from '@app/api/api/chats.service';
import { MessagesService } from '@app/api/api/messages.service';
import { ReactionsService } from '@app/api/api/reactions.service';
import { toChatMember, toChatMembers, toMessage, toMessages } from '@app/chat/message.mapper';
import { MessageListComponent } from '@app/chat/message-list/message-list';
import { MessageInputComponent } from '@app/chat/message-input/message-input';
import { TypingIndicatorComponent } from '@app/chat/typing-indicator/typing-indicator';
import { BotManagerComponent } from '@app/bots/bot-manager/bot-manager';
import { firstValueFrom, map } from 'rxjs';

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
  private readonly auth = inject(AuthService);
  private readonly signalr = inject(SignalRService);
  private readonly chatsApi = inject(ChatsService);
  private readonly messagesApi = inject(MessagesService);
  private readonly reactionsApi = inject(ReactionsService);

  readonly chat = signal<ChatDetailDto | null>(null);
  readonly messages = signal<Message[]>([]);
  readonly members = signal<ChatMember[]>([]);
  readonly loading = signal(true);
  readonly botManagerOpen = signal(false);

  readonly typingUsers = signal<Set<string>>(new Set());
  readonly typingBots = signal<Map<string, string>>(new Map());

  readonly chatName = computed(() => this.chat()?.name ?? '');
  readonly memberCount = computed(() => this.members().length);
  readonly userCount = computed(() => this.members().filter((m) => m.memberType === 'user').length);
  readonly botCount = computed(() => this.members().filter((m) => m.memberType === 'bot').length);
  readonly botMembers = computed(() => this.members().filter((m) => m.memberType === 'bot'));
  readonly replyingTo = signal<Message | null>(null);

  currentChatId: string | null = null;

  private onMessageNew = (dto: MessageDto) => {
    this.messages.update((list) => [...list, toMessage(dto)]);
  };

  private onMemberAdded = (dto: ChatMemberDto) => {
    const member = toChatMember(dto);
    this.members.update((list) =>
      list.some((m) => m.id === member.id) ? list : [...list, member],
    );
  };

  private onMemberRemoved = (_chatId: string, memberId: string) => {
    this.members.update((list) => list.filter((m) => m.id !== memberId));
  };

  private onBotTyping = (_chatId: string, botId: string, botName: string) => {
    this.typingBots.update((map) => new Map(map).set(botId, botName));
    setTimeout(() => {
      this.typingBots.update((map) => {
        const next = new Map(map);
        next.delete(botId);
        return next;
      });
    }, 5000);
  };

  private onReactionUpdated = (messageId: string, reactions: ReactionGroupDto[]) => {
    this.messages.update((list) =>
      list.map((m) => (m.id === messageId ? { ...m, reactions: reactions ?? [] } : m)),
    );
  };

  private onChatCleared = () => {
    this.messages.set([]);
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
  }

  async sendMessage(event: { content: string; mentions: string[] }) {
    if (!this.currentChatId) return;
    const replyToId = this.replyingTo()?.id;
    try {
      await firstValueFrom(
        this.messagesApi.apiChatsChatIdMessagesPost(this.currentChatId, { content: event.content, replyToId }),
      );
      this.replyingTo.set(null);
    } catch {
      toast.error('Message not sent. Check your connection and try again.');
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

  private async loadChat(chatId: string) {
    this.unsubscribeEvents();

    const previousChatId = this.currentChatId;
    if (previousChatId) {
      void this.signalr.leaveChat(previousChatId).catch(() => undefined);
    }

    this.currentChatId = chatId;
    this.loading.set(true);

    try {
      const chat = await firstValueFrom(this.chatsApi.apiChatsChatIdGet(chatId));
      this.chat.set(chat);
      this.members.set(toChatMembers(chat.members));

      const dtos = await firstValueFrom(this.messagesApi.apiChatsChatIdMessagesGet(chatId));
      this.messages.set(toMessages(dtos));

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
    this.signalr.onChatEvent('ReactionUpdated', this.onReactionUpdated);
    this.signalr.onChatEvent('ChatCleared', this.onChatCleared);
  }

  private unsubscribeEvents() {
    this.signalr.offChatEvent('MessageNew', this.onMessageNew);
    this.signalr.offChatEvent('MemberAdded', this.onMemberAdded);
    this.signalr.offChatEvent('MemberRemoved', this.onMemberRemoved);
    this.signalr.offChatEvent('BotTyping', this.onBotTyping);
    this.signalr.offChatEvent('ReactionUpdated', this.onReactionUpdated);
    this.signalr.offChatEvent('ChatCleared', this.onChatCleared);
  }
}
