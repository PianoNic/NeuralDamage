import { Component, computed, effect, inject, input, OnDestroy, output, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucidePencil, lucidePlus, lucideTrash2, lucideUserPlus, lucideX } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertDialogImports } from '@spartan-ng/helm/alert-dialog';
import { HlmAvatarImports } from '@spartan-ng/helm/avatar';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmInput } from '@spartan-ng/helm/input';
import { HlmSeparator } from '@spartan-ng/helm/separator';
import { HlmSheetImports } from '@spartan-ng/helm/sheet';
import { firstValueFrom } from 'rxjs';
import { ChatMembersService } from '../../api/api/chatMembers.service';
import { UserService } from '../../api/api/user.service';
import { describeApiError } from '../../core/http-errors';
import { BotDto, ChatMember, UserDto } from '../../core/models';
import { initials } from '../../shared/initials';
import { BotDirectory } from '../bots/bot-directory';
import { BotForm } from './bot-form';

/**
 * Who is in the chat, in a sheet from the right: people (invite more), bots in the chat, and the
 * bots that could join. Issue #54 redesigns this; the behaviour is the members panel's.
 */
@Component({
  selector: 'app-people-sheet',
  imports: [
    NgIcon,
    HlmAlertDialogImports,
    HlmAvatarImports,
    HlmBadge,
    HlmButton,
    HlmDialogImports,
    HlmInput,
    HlmSeparator,
    HlmSheetImports,
    BotForm,
  ],
  providers: [provideIcons({ lucidePencil, lucidePlus, lucideTrash2, lucideUserPlus, lucideX })],
  template: `
    <hlm-sheet side="right" [state]="open() ? 'open' : 'closed'" (closed)="closed.emit()">
      <hlm-sheet-content *hlmSheetPortal="let ctx" class="w-full gap-0 p-0 sm:max-w-sm">
        <hlm-sheet-header class="border-b">
          <h2 hlmSheetTitle>People</h2>
          <p hlmSheetDescription>{{ bots().length }} bots, {{ people().length }} people</p>
        </hlm-sheet-header>

        <div class="flex min-h-0 flex-1 flex-col gap-6 overflow-y-auto p-4">
          <section class="flex flex-col gap-1">
            <h3 class="text-muted-foreground px-2 text-xs font-medium">People</h3>
            @for (member of people(); track member.id) {
              <div class="hover:bg-muted/50 flex items-center gap-3 rounded-md px-2 py-1.5">
                <hlm-avatar size="sm">
                  @if (member.avatarUrl) {
                    <img hlmAvatarImage [src]="member.avatarUrl" alt="" />
                  }
                  <span hlmAvatarFallback class="text-[10px]">{{ initialsOf(member.displayName) }}</span>
                </hlm-avatar>
                <span class="flex-1 truncate text-sm">{{ member.displayName }}</span>
                @if (member.role === 'Owner') {
                  <span hlmBadge variant="outline">Owner</span>
                } @else {
                  <button
                    hlmBtn
                    variant="ghost"
                    size="icon-xs"
                    [attr.aria-label]="'Remove ' + member.displayName"
                    (click)="removeMember(member.id)"
                  >
                    <ng-icon name="lucideX" />
                  </button>
                }
              </div>
            }

            <input
              hlmInput
              type="search"
              class="mt-2"
              placeholder="Invite by name or email…"
              aria-label="Search people to invite"
              [value]="inviteQuery()"
              (input)="inviteQuery.set($any($event.target).value)"
            />
            @for (user of invitable(); track user.id) {
              <div class="hover:bg-muted/50 flex items-center gap-3 rounded-md px-2 py-1.5">
                <hlm-avatar size="sm">
                  @if (user.avatarUrl) {
                    <img hlmAvatarImage [src]="user.avatarUrl" alt="" />
                  }
                  <span hlmAvatarFallback class="text-[10px]">{{ initialsOf(user.displayName || user.email) }}</span>
                </hlm-avatar>
                <div class="min-w-0 flex-1 leading-tight">
                  <span class="block truncate text-sm">{{ user.displayName || user.email }}</span>
                  @if (user.displayName) {
                    <span class="text-muted-foreground block truncate text-xs">{{ user.email }}</span>
                  }
                </div>
                <button
                  hlmBtn
                  variant="outline"
                  size="icon-xs"
                  [attr.aria-label]="'Invite ' + (user.displayName || user.email)"
                  (click)="invite(user.id)"
                >
                  <ng-icon name="lucideUserPlus" />
                </button>
              </div>
            } @empty {
              <p class="text-muted-foreground px-2 text-xs">
                {{ inviteQuery().trim() ? 'Nobody matches.' : 'Everyone is already here.' }}
              </p>
            }
          </section>

          <section class="flex flex-col gap-1">
            <h3 class="text-muted-foreground px-2 text-xs font-medium">Bots in this chat</h3>
            @for (member of bots(); track member.id) {
              <div class="hover:bg-muted/50 flex items-center gap-3 rounded-md px-2 py-1.5">
                <hlm-avatar size="sm">
                  @if (member.avatarUrl) {
                    <img hlmAvatarImage [src]="member.avatarUrl" alt="" />
                  }
                  <span hlmAvatarFallback class="text-[10px]">{{ initialsOf(member.displayName) }}</span>
                </hlm-avatar>
                <span class="flex-1 truncate text-sm">{{ member.displayName }}</span>
                <span hlmBadge variant="secondary">Bot</span>
                <button
                  hlmBtn
                  variant="ghost"
                  size="icon-xs"
                  [attr.aria-label]="'Remove ' + member.displayName"
                  (click)="removeMember(member.id)"
                >
                  <ng-icon name="lucideX" />
                </button>
              </div>
            } @empty {
              <p class="text-muted-foreground px-2 text-xs">No bots yet. Add one below.</p>
            }
          </section>

          <hlm-separator />

          <section class="flex flex-col gap-1">
            <h3 class="text-muted-foreground px-2 text-xs font-medium">Add a bot</h3>
            <input
              hlmInput
              type="search"
              class="mb-1"
              placeholder="Search bots…"
              aria-label="Search bots"
              [value]="botQuery()"
              (input)="botQuery.set($any($event.target).value)"
            />
            @for (bot of available(); track bot.id) {
              <div class="hover:bg-muted/50 flex items-center gap-3 rounded-md px-2 py-1.5">
                <hlm-avatar size="sm">
                  @if (bot.avatarUrl) {
                    <img hlmAvatarImage [src]="bot.avatarUrl" alt="" />
                  }
                  <span hlmAvatarFallback class="text-[10px]">{{ initialsOf(bot.name) }}</span>
                </hlm-avatar>
                <div class="min-w-0 flex-1 leading-tight">
                  <span class="block truncate text-sm">{{ bot.name }}</span>
                  <span class="text-muted-foreground block truncate text-xs">{{ bot.modelId }}</span>
                </div>
                <button hlmBtn variant="outline" size="icon-xs" [attr.aria-label]="'Add ' + bot.name" (click)="addBot(bot.id)">
                  <ng-icon name="lucidePlus" />
                </button>
                <button hlmBtn variant="ghost" size="icon-xs" [attr.aria-label]="'Edit ' + bot.name" (click)="editBot(bot)">
                  <ng-icon name="lucidePencil" />
                </button>
                <button hlmBtn variant="ghost" size="icon-xs" [attr.aria-label]="'Delete ' + bot.name" (click)="deletingBot.set(bot)">
                  <ng-icon name="lucideTrash2" />
                </button>
              </div>
            } @empty {
              <p class="text-muted-foreground px-2 text-xs">No other bots.</p>
            }
            <button hlmBtn variant="outline" class="mt-2" (click)="editBot(null)">
              <ng-icon name="lucidePlus" />
              Create a bot
            </button>
          </section>
        </div>
      </hlm-sheet-content>
    </hlm-sheet>

    <hlm-dialog [state]="formOpen() ? 'open' : 'closed'" (closed)="closeForm()">
      <hlm-dialog-content *hlmDialogPortal="let ctx" class="max-h-[90vh] overflow-y-auto sm:max-w-2xl">
        <hlm-dialog-header>
          <h2 hlmDialogTitle>{{ editing() ? 'Edit bot' : 'Create a bot' }}</h2>
          <p hlmDialogDescription>Its model, instructions and personality.</p>
        </hlm-dialog-header>
        @if (formOpen()) {
          <app-bot-form [bot]="editing()" (saved)="onSaved(); ctx.close()" (cancel)="ctx.close()" />
        }
      </hlm-dialog-content>
    </hlm-dialog>

    <hlm-alert-dialog [state]="deletingBot() ? 'open' : 'closed'" (closed)="deletingBot.set(null)">
      <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
        <hlm-alert-dialog-header>
          <h2 hlmAlertDialogTitle>Delete {{ deletingBot()?.name }}?</h2>
          <p hlmAlertDialogDescription>
            The bot is deleted for good. Messages it already sent stay.
          </p>
        </hlm-alert-dialog-header>
        <hlm-alert-dialog-footer>
          <button hlmAlertDialogCancel (click)="ctx.close()">Cancel</button>
          <button hlmAlertDialogAction variant="destructive" (click)="deleteBot(); ctx.close()">Delete</button>
        </hlm-alert-dialog-footer>
      </hlm-alert-dialog-content>
    </hlm-alert-dialog>
  `,
})
export class PeopleSheet implements OnDestroy {
  private readonly directory = inject(BotDirectory);
  private readonly membersApi = inject(ChatMembersService);
  private readonly usersApi = inject(UserService);

  readonly open = input(false);
  readonly chatId = input.required<string>();
  readonly members = input.required<ChatMember[]>();
  readonly closed = output();

  protected readonly botQuery = signal('');
  protected readonly inviteQuery = signal('');
  protected readonly invitable = signal<UserDto[]>([]);
  protected readonly formOpen = signal(false);
  protected readonly editing = signal<BotDto | null>(null);
  protected readonly deletingBot = signal<BotDto | null>(null);
  private inviteTimer?: ReturnType<typeof setTimeout>;

  protected readonly people = computed(() => this.members().filter((m) => m.memberType === 'user'));
  protected readonly bots = computed(() => this.members().filter((m) => m.memberType === 'bot'));
  protected readonly available = computed(() => {
    const inChat = new Set(this.bots().map((m) => m.botId));
    const query = this.botQuery().trim().toLowerCase();
    return this.directory
      .bots()
      .filter((b) => !inChat.has(b.id) && (!query || b.name.toLowerCase().includes(query)));
  });

  constructor() {
    // Re-query when the search changes (debounced) or someone joins or leaves, since the endpoint
    // leaves out the chat's current members. Only while the sheet is open.
    effect(() => {
      const open = this.open();
      const chatId = this.chatId();
      const search = this.inviteQuery().trim();
      this.members();
      clearTimeout(this.inviteTimer);
      if (!open || !chatId) return;
      this.inviteTimer = setTimeout(() => void this.loadInvitable(chatId, search), 250);
    });
    effect(() => {
      if (this.open()) void this.directory.load();
    });
  }

  ngOnDestroy(): void {
    clearTimeout(this.inviteTimer);
  }

  protected initialsOf(name: string): string {
    return initials(name);
  }

  protected async addBot(botId: string): Promise<void> {
    try {
      await firstValueFrom(this.membersApi.apiChatsChatIdMembersPost(this.chatId(), { botId }));
    } catch (error) {
      toast.error(describeApiError(error, { fallback: 'Could not add that bot to the chat.' }));
    }
  }

  protected async removeMember(memberId: string): Promise<void> {
    try {
      await firstValueFrom(this.membersApi.apiChatsChatIdMembersMemberIdDelete(this.chatId(), memberId));
    } catch (error) {
      toast.error(describeApiError(error, { fallback: 'Could not remove that member.' }));
    }
  }

  protected async invite(userId: string): Promise<void> {
    try {
      await firstValueFrom(this.membersApi.apiChatsChatIdMembersPost(this.chatId(), { userId }));
      this.invitable.update((list) => list.filter((u) => u.id !== userId));
    } catch (error) {
      toast.error(describeApiError(error, { fallback: 'Could not invite that person.' }));
    }
  }

  protected editBot(bot: BotDto | null): void {
    this.editing.set(bot);
    this.formOpen.set(true);
  }

  protected closeForm(): void {
    this.formOpen.set(false);
    this.editing.set(null);
  }

  protected async onSaved(): Promise<void> {
    this.closeForm();
    await this.directory.reload();
  }

  protected async deleteBot(): Promise<void> {
    const bot = this.deletingBot();
    if (!bot) return;
    try {
      await this.directory.remove(bot.id);
    } catch (error) {
      toast.error(describeApiError(error, { fallback: 'Could not delete that bot.' }));
    }
  }

  private async loadInvitable(chatId: string, search: string): Promise<void> {
    try {
      this.invitable.set(await firstValueFrom(this.usersApi.getUsers(search || undefined, chatId)));
    } catch {
      this.invitable.set([]);
    }
  }
}
