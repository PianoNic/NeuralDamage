import { ChangeDetectionStrategy, Component, computed, inject, input, OnInit, output, signal } from '@angular/core';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmInput } from '@spartan-ng/helm/input';
import { HlmSeparator } from '@spartan-ng/helm/separator';
import { HlmAlertDialogImports } from '@spartan-ng/helm/alert-dialog';
import { HlmAvatar, HlmAvatarFallback, HlmAvatarImage } from '@spartan-ng/helm/avatar';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideX, lucidePlus, lucidePencil, lucideTrash2, lucideUsers, lucideBot } from '@ng-icons/lucide';
import { Bot, ChatMember } from '@app/models';
import { BotFormComponent } from '@app/bots/bot-form/bot-form';
import { BotsService } from '@app/api/api/bots.service';
import { ChatMembersService } from '@app/api/api/chatMembers.service';
import { ChatActionsService } from '@app/api/api/chatActions.service';
import { firstValueFrom } from 'rxjs';

@Component({
  selector: 'app-bot-manager',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [HlmButton, HlmInput, HlmSeparator, HlmAlertDialogImports, HlmAvatar, HlmAvatarFallback, HlmAvatarImage, HlmBadge, NgIcon, BotFormComponent],
  viewProviders: [provideIcons({ lucideX, lucidePlus, lucidePencil, lucideTrash2, lucideUsers, lucideBot })],
  templateUrl: './bot-manager.html',
})
export class BotManagerComponent implements OnInit {
  private readonly botsApi = inject(BotsService);
  private readonly membersApi = inject(ChatMembersService);
  private readonly actionsApi = inject(ChatActionsService);

  readonly chatId = input.required<string>();
  readonly members = input.required<ChatMember[]>();
  readonly close = output<void>();

  readonly allBots = signal<Bot[]>([]);
  readonly searchQuery = signal('');
  readonly showBotForm = signal(false);
  readonly editingBot = signal<Bot | null>(null);
  readonly showDeleteBotDialog = signal(false);
  readonly deletingBotId = signal<string | null>(null);

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

  async ngOnInit() {
    const bots = (await firstValueFrom(this.botsApi.apiBotsGet())) as Bot[];
    this.allBots.set(bots);
  }

  async addBot(botId: string) {
    await firstValueFrom(this.membersApi.apiChatsChatIdMembersPost(this.chatId(), { botId }));
  }

  async removeBot(memberId: string) {
    await firstValueFrom(this.membersApi.apiChatsChatIdMembersMemberIdDelete(this.chatId(), memberId));
  }

  openBotForm(bot?: Bot) {
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
    await firstValueFrom(this.botsApi.apiBotsBotIdDelete(botId));
    this.allBots.update((list) => list.filter((b) => b.id !== botId));
    this.showDeleteBotDialog.set(false);
    this.deletingBotId.set(null);
  }

  async onBotSaved() {
    this.closeBotForm();
    const bots = (await firstValueFrom(this.botsApi.apiBotsGet())) as Bot[];
    this.allBots.set(bots);
  }
}
