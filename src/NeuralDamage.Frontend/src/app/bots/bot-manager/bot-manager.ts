import { toast } from '@spartan-ng/brain/sonner';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, OnDestroy, OnInit, output, signal } from '@angular/core';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmInput } from '@spartan-ng/helm/input';
import { HlmSeparator } from '@spartan-ng/helm/separator';
import { HlmAlertDialogImports } from '@spartan-ng/helm/alert-dialog';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmAvatar, HlmAvatarFallback, HlmAvatarImage } from '@spartan-ng/helm/avatar';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideX, lucidePlus, lucidePencil, lucideTrash2, lucideUsers, lucideBot, lucideUserPlus } from '@ng-icons/lucide';
import { BotDto, ChatMember, UserDto } from '@app/models';
import { BotFormComponent } from '@app/bots/bot-form/bot-form';
import { BotsService } from '@app/api/api/bots.service';
import { ChatMembersService } from '@app/api/api/chatMembers.service';
import { ChatActionsService } from '@app/api/api/chatActions.service';
import { UserService } from '@app/api/api/user.service';
import { firstValueFrom } from 'rxjs';

@Component({
  selector: 'app-bot-manager',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmButton, HlmInput, HlmSeparator, HlmAlertDialogImports, HlmDialogImports, HlmAvatar, HlmAvatarFallback, HlmAvatarImage, HlmBadge, NgIcon, BotFormComponent],
  viewProviders: [provideIcons({ lucideX, lucidePlus, lucidePencil, lucideTrash2, lucideUsers, lucideBot, lucideUserPlus })],
  templateUrl: './bot-manager.html',
})
export class BotManagerComponent implements OnInit, OnDestroy {
  private readonly botsApi = inject(BotsService);
  private readonly membersApi = inject(ChatMembersService);
  private readonly actionsApi = inject(ChatActionsService);
  private readonly usersApi = inject(UserService);

  readonly chatId = input.required<string>();
  readonly members = input.required<ChatMember[]>();
  readonly close = output<void>();

  readonly allBots = signal<BotDto[]>([]);
  readonly searchQuery = signal('');
  readonly showBotForm = signal(false);
  readonly editingBot = signal<BotDto | null>(null);
  readonly showDeleteBotDialog = signal(false);
  readonly deletingBotId = signal<string | null>(null);

  readonly inviteQuery = signal('');
  readonly invitableUsers = signal<UserDto[]>([]);
  private inviteTimer?: ReturnType<typeof setTimeout>;

  readonly botsInChat = computed(() =>
    this.members().filter((m) => m.memberType === 'bot'),
  );

  readonly usersInChat = computed(() =>
    this.members().filter((m) => m.memberType === 'user'),
  );

  readonly availableBots = computed(() => {
    const inChat = new Set(this.botsInChat().map((m) => m.botId));
    const query = this.searchQuery().toLowerCase();
    return this.allBots()
      .filter((b) => !inChat.has(b.id))
      .filter((b) => !query || b.name.toLowerCase().includes(query));
  });

  constructor() {
    // Re-query when the search changes (debounced) or someone joins or leaves,
    // since the endpoint leaves out the chat's current members.
    effect(() => {
      const chatId = this.chatId();
      const search = this.inviteQuery().trim();
      this.members();
      clearTimeout(this.inviteTimer);
      this.inviteTimer = setTimeout(() => void this.loadInvitableUsers(chatId, search), 250);
    });
  }

  ngOnDestroy() {
    clearTimeout(this.inviteTimer);
  }

  async ngOnInit() {
    const bots = await firstValueFrom(this.botsApi.apiBotsGet());
    this.allBots.set(bots);
  }

  async addBot(botId: string) {
    try {
      await firstValueFrom(this.membersApi.apiChatsChatIdMembersPost(this.chatId(), { botId }));
    } catch (error: unknown) {
      toast.error(serverMessage(error) ?? 'Could not add that bot to the chat.');
    }
  }

  async removeBot(memberId: string) {
    try {
      await firstValueFrom(this.membersApi.apiChatsChatIdMembersMemberIdDelete(this.chatId(), memberId));
    } catch (error: unknown) {
      toast.error(serverMessage(error) ?? 'Could not remove that member.');
    }
  }

  async inviteUser(userId: string) {
    try {
      await firstValueFrom(this.membersApi.apiChatsChatIdMembersPost(this.chatId(), { userId }));
      this.invitableUsers.update((list) => list.filter((u) => u.id !== userId));
    } catch (error: unknown) {
      toast.error(serverMessage(error) ?? 'Could not invite that user.');
    }
  }

  private async loadInvitableUsers(chatId: string, search: string) {
    if (!chatId) return;
    try {
      const users = await firstValueFrom(this.usersApi.getUsers(search || undefined, chatId));
      this.invitableUsers.set(users);
    } catch {
      this.invitableUsers.set([]);
    }
  }

  openBotForm(bot?: BotDto) {
    this.editingBot.set(bot ?? null);
    this.showBotForm.set(true);
  }

  closeBotForm() {
    this.showBotForm.set(false);
    this.editingBot.set(null);
  }

  confirmDeleteBot(botId: string) {
    this.deletingBotId.set(botId);
    this.showDeleteBotDialog.set(true);
  }

  async executeDeleteBot() {
    const botId = this.deletingBotId();
    if (!botId) return;
    try {
      await firstValueFrom(this.botsApi.apiBotsBotIdDelete(botId));
    } catch {
      toast.error('Could not delete that bot.');
    }
    this.allBots.update((list) => list.filter((b) => b.id !== botId));
    this.showDeleteBotDialog.set(false);
    this.deletingBotId.set(null);
  }

  async onBotSaved() {
    this.closeBotForm();
    const bots = await firstValueFrom(this.botsApi.apiBotsGet());
    this.allBots.set(bots);
  }
}

/** ProblemDetails or a plain string body from the API, when there is one. */
function serverMessage(error: unknown): string | null {
  const body = (error as { error?: unknown })?.error;
  if (typeof body === 'string' && body.trim()) return body;
  const detail = (body as { detail?: string; title?: string } | undefined)?.detail
    ?? (body as { title?: string } | undefined)?.title;
  return detail?.trim() ? detail : null;
}
