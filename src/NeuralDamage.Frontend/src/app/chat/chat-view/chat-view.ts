import { ChangeDetectionStrategy, Component, computed, effect, inject, OnDestroy, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { HlmAvatar, HlmAvatarFallback, HlmAvatarImage } from '@spartan-ng/helm/avatar';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideBot, lucideUsers } from '@ng-icons/lucide';
import { Chat, ChatMember, Message } from '@app/models';
import { AuthService } from '@app/shared/auth/auth.service';
import { SignalRService } from '@app/shared/signalr/signalr.service';
import { ChatsService } from '@app/api/api/chats.service';
import { MessagesService } from '@app/api/api/messages.service';
import { ReactionsService } from '@app/api/api/reactions.service';
import { MessageListComponent } from '@app/chat/message-list/message-list';
import { MessageInputComponent } from '@app/chat/message-input/message-input';
import { TypingIndicatorComponent } from '@app/chat/typing-indicator/typing-indicator';
import { BotManagerComponent } from '@app/bots/bot-manager/bot-manager';
import { firstValueFrom } from 'rxjs';

@Component({
  selector: 'app-chat-view',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmButton, HlmSpinner, HlmAvatar, HlmAvatarFallback, HlmAvatarImage, NgIcon, MessageListComponent, MessageInputComponent, TypingIndicatorComponent, BotManagerComponent],
  viewProviders: [provideIcons({ lucideBot, lucideUsers })],
  templateUrl: './chat-view.html',
  host: { class: 'flex flex-1' },
})
export class ChatViewComponent implements OnDestroy {
  private readonly route = inject(ActivatedRoute);
  private readonly auth = inject(AuthService);
  private readonly signalr = inject(SignalRService);
  private readonly chatsApi = inject(ChatsService);
  private readonly messagesApi = inject(MessagesService);
  private readonly reactionsApi = inject(ReactionsService);

  readonly chat = signal<Chat | null>(null);
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

  private onMessageNew = (msg: Message) => {
    this.messages.update((list) => [...list, msg]);
  };

  private onMemberAdded = (member: ChatMember) => {
    this.members.update((list) => [...list, member]);
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

  private onChatCleared = () => {
    this.messages.set([]);
  };

  constructor() {
    effect(() => {
      const params = this.route.snapshot.paramMap;
      const chatId = params.get('chatId');
      if (chatId && chatId !== this.currentChatId) {
        this.loadChat(chatId);
      }
    });
  }

  ngOnDestroy() {
    this.unsubscribeEvents();
  }

  async sendMessage(event: { content: string; mentions: string[] }) {
    if (!this.currentChatId) return;
    const replyToId = this.replyingTo()?.id;
    await firstValueFrom(
      this.messagesApi.apiChatsChatIdMessagesPost(this.currentChatId, { content: event.content, replyToId }),
    );
    this.replyingTo.set(null);
  }

  async toggleReaction(messageId: string, emoji: string) {
    if (!this.currentChatId) return;
    await firstValueFrom(this.reactionsApi.apiChatsChatIdMessagesMessageIdReactionsEmojiPost(this.currentChatId, messageId, emoji));
  }

  toggleBotManager() {
    this.botManagerOpen.update((v) => !v);
  }

  setReplyTo(message: Message | null) {
    this.replyingTo.set(message);
  }

  private async loadChat(chatId: string) {
    this.unsubscribeEvents();
    this.currentChatId = chatId;
    this.loading.set(true);

    try {
      const chat = (await firstValueFrom(this.chatsApi.apiChatsChatIdGet(chatId))) as any;
      this.chat.set(chat);
      this.members.set(chat.members ?? []);

      const messages = (await firstValueFrom(this.messagesApi.apiChatsChatIdMessagesGet(chatId))) as Message[];
      this.messages.set(messages);

      this.subscribeEvents();
    } catch (e) {
      console.error('Failed to load chat', e);
    }

    this.loading.set(false);
  }

  private subscribeEvents() {
    this.signalr.onChatEvent('MessageNew', this.onMessageNew);
    this.signalr.onChatEvent('MemberAdded', this.onMemberAdded);
    this.signalr.onChatEvent('MemberRemoved', this.onMemberRemoved);
    this.signalr.onChatEvent('BotTyping', this.onBotTyping);
    this.signalr.onChatEvent('ChatCleared', this.onChatCleared);
  }

  private unsubscribeEvents() {
    this.signalr.offChatEvent('MessageNew', this.onMessageNew);
    this.signalr.offChatEvent('MemberAdded', this.onMemberAdded);
    this.signalr.offChatEvent('MemberRemoved', this.onMemberRemoved);
    this.signalr.offChatEvent('BotTyping', this.onBotTyping);
    this.signalr.offChatEvent('ChatCleared', this.onChatCleared);
  }
}
