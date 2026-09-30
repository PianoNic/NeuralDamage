import {
  Component,
  computed,
  effect,
  ElementRef,
  inject,
  input,
  OnDestroy,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideAtSign,
  lucideEllipsis,
  lucideLogOut,
  lucidePencil,
  lucidePlus,
  lucideSearch,
  lucideTriangleAlert,
  lucideUserMinus,
  lucideVolume2,
  lucideVolumeX,
  lucideX,
} from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmInputGroupImports } from '@spartan-ng/helm/input-group';
import { HlmPopoverImports } from '@spartan-ng/helm/popover';
import { HlmSheetImports } from '@spartan-ng/helm/sheet';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { firstValueFrom } from 'rxjs';
import { BotsService } from '../../api/api/bots.service';
import { ChatBotsService } from '../../api/api/chatBots.service';
import { ChatMembersService } from '../../api/api/chatMembers.service';
import { UserService } from '../../api/api/user.service';
import { describeApiError } from '../../core/http-errors';
import { mediaQuery } from '../../core/media-query';
import { BotDto, ChatMember, UserDto } from '../../core/models';
import { MemberAvatar } from '../../shared/member-avatar';
import { BotDialog } from '../bots/bot-dialog';
import { BotDirectory } from '../bots/bot-directory';
import { hasModelProblem, modelProblemLabel } from '../bots/bot-meta';
import { MemberProfile } from './member-profile';
import { canRemoveMember, joinedLabel } from './people-meta';

/** Vertical room the profile card needs before it is pushed up from the row it belongs to. */
const PROFILE_HEIGHT = 340;

/**
 * Who is in the chat, in a sheet from the right: bots, then people. A row opens a profile card beside
 * the sheet; its menu has the quick actions. "Add" invites people and adds public bots, or makes a
 * new bot for this chat.
 */
@Component({
  selector: 'app-people-sheet',
  imports: [
    NgIcon,
    HlmBadge,
    HlmButton,
    HlmDropdownMenuImports,
    HlmInputGroupImports,
    HlmPopoverImports,
    HlmSheetImports,
    HlmTooltipImports,
    MemberAvatar,
    MemberProfile,
    BotDialog,
  ],
  providers: [
    provideIcons({
      lucideAtSign,
      lucideEllipsis,
      lucideLogOut,
      lucidePencil,
      lucidePlus,
      lucideSearch,
      lucideTriangleAlert,
      lucideUserMinus,
      lucideVolume2,
      lucideVolumeX,
      lucideX,
    }),
  ],
  template: `
    <hlm-sheet side="right" [state]="open() ? 'open' : 'closed'" (closed)="onSheetClosed()">
      <hlm-sheet-content
        *hlmSheetPortal="let ctx"
        [showCloseButton]="false"
        class="w-full gap-0 p-0 sm:max-w-sm"
      >
        <div #panel class="flex min-h-0 flex-1 flex-col gap-3.5 p-4">
          <div class="flex items-start gap-2">
            <div class="flex flex-1 flex-col gap-1">
              <h2 hlmSheetTitle>People</h2>
              <p hlmSheetDescription>{{ members().length }} in this chat</p>
            </div>

            <hlm-popover align="end" [sideOffset]="6" (stateChanged)="addOpen.set($event === 'open')">
              <button hlmBtn hlmPopoverTrigger variant="outline" size="sm">
                <ng-icon name="lucidePlus" />
                Add
              </button>
              <hlm-popover-content *hlmPopoverPortal="let popover" class="w-80 gap-1 p-2">
                <div hlmInputGroup class="mb-1">
                  <input
                    hlmInputGroupInput
                    type="search"
                    placeholder="Search people and bots"
                    aria-label="Search people and bots to add"
                    [value]="addQuery()"
                    (input)="addQuery.set($any($event.target).value)"
                  />
                  <div hlmInputGroupAddon><ng-icon name="lucideSearch" /></div>
                </div>
                <div class="flex max-h-80 flex-col gap-0.5 overflow-y-auto">
                  @if (addableBots().length) {
                    <p class="text-muted-foreground px-2 pt-1 text-xs font-medium">Public bots</p>
                    @for (bot of addableBots(); track bot.id) {
                      <button type="button" [class]="pickClass" (click)="addBot(bot)">
                        <app-member-avatar [name]="bot.name" [avatarUrl]="bot.avatarUrl" [modelId]="bot.modelId" [px]="24" />
                        <span class="flex min-w-0 flex-1 flex-col leading-tight">
                          <span class="truncate text-sm">{{ bot.name }}</span>
                          @if (bot.personality) {
                            <span class="text-muted-foreground truncate text-xs">{{ bot.personality }}</span>
                          }
                        </span>
                        <ng-icon name="lucidePlus" class="text-muted-foreground" />
                      </button>
                    }
                  }
                  @if (invitable().length) {
                    <p class="text-muted-foreground px-2 pt-1 text-xs font-medium">People</p>
                    @for (user of invitable(); track user.id) {
                      <button type="button" [class]="pickClass" (click)="invite(user)">
                        <app-member-avatar [name]="user.displayName || user.email" [avatarUrl]="user.avatarUrl" [px]="24" />
                        <span class="flex min-w-0 flex-1 flex-col leading-tight">
                          <span class="truncate text-sm">{{ user.displayName || user.email }}</span>
                          @if (user.displayName) {
                            <span class="text-muted-foreground truncate text-xs">{{ user.email }}</span>
                          }
                        </span>
                        <ng-icon name="lucidePlus" class="text-muted-foreground" />
                      </button>
                    }
                  }
                  @if (!addableBots().length && !invitable().length) {
                    <p class="text-muted-foreground px-2 py-3 text-center text-xs">
                      {{ addQuery().trim() ? 'Nothing matches.' : 'Everyone is already here.' }}
                    </p>
                  }
                </div>
                <button hlmBtn variant="ghost" size="sm" class="mt-1 justify-start" (click)="popover.close(); newBot()">
                  <ng-icon name="lucidePlus" />
                  New bot for this chat
                </button>
              </hlm-popover-content>
            </hlm-popover>

            <button hlmBtn variant="ghost" size="icon-sm" aria-label="Close" (click)="ctx.close()">
              <ng-icon name="lucideX" />
            </button>
          </div>

          <div class="-mx-1 flex min-h-0 flex-1 flex-col gap-3.5 overflow-y-auto px-1">
            @for (group of groups(); track group.label) {
              <section class="flex flex-col" [attr.aria-label]="group.label">
                <h3 class="text-muted-foreground flex items-center gap-1.5 pb-1 text-xs font-medium">
                  {{ group.label }}<span class="tabular-nums">{{ group.members.length }}</span>
                </h3>
                <ul class="-mx-1 flex flex-col gap-0.5">
                  @for (member of group.members; track member.id) {
                    <li
                      class="group/row hover:bg-muted/60 flex items-center gap-0.5 rounded-lg pe-1"
                      [class.bg-muted]="profileId() === member.id"
                    >
                      <button
                        type="button"
                        class="focus-visible:ring-ring/50 flex h-11 min-w-0 flex-1 items-center gap-2.5 rounded-lg px-2 text-left outline-none focus-visible:ring-2"
                        [attr.aria-label]="'Open ' + member.displayName + '\\'s profile'"
                        [attr.aria-expanded]="profileId() === member.id"
                        (click)="toggleProfile(member, $event)"
                      >
                        <app-member-avatar [name]="member.displayName" [avatarUrl]="member.avatarUrl" [modelId]="member.modelId" [px]="28" />
                        <span class="flex min-w-0 flex-col leading-snug">
                          <span class="flex min-w-0 items-center gap-1.5">
                            <span class="truncate text-sm font-medium">{{ member.displayName }}</span>
                            @if (member.role === 'Owner') {
                              <span hlmBadge variant="secondary">Owner</span>
                            }
                            @if (member.isMuted) {
                              <span hlmBadge variant="outline">Muted</span>
                            }
                            @if (isPrivate(member)) {
                              <span hlmBadge variant="outline">Private</span>
                            }
                            @if (broken(member)) {
                              <span
                                hlmBadge
                                variant="destructive"
                                tabindex="0"
                                [hlmTooltip]="member.modelStatusReason || problemLabel(member)"
                                [attr.aria-label]="problemLabel(member) + ': ' + (member.modelStatusReason || '')"
                              >
                                <ng-icon name="lucideTriangleAlert" />
                                {{ problemLabel(member) }}
                              </span>
                            }
                          </span>
                          <span class="text-muted-foreground truncate text-xs">{{ lineFor(member) }}</span>
                        </span>
                      </button>

                      <button
                        hlmBtn
                        variant="ghost"
                        size="icon-sm"
                        class="opacity-0 group-hover/row:opacity-100 focus-visible:opacity-100 aria-expanded:opacity-100"
                        [attr.aria-label]="'Actions for ' + member.displayName"
                        [hlmDropdownMenuTrigger]="menu"
                        [hlmDropdownMenuTriggerData]="{ $implicit: member }"
                        align="end"
                      >
                        <ng-icon name="lucideEllipsis" />
                      </button>
                    </li>
                  }
                </ul>
              </section>
            }
          </div>
        </div>

        @if (desktop() && profileMember(); as member) {
          <app-member-profile
            class="absolute right-[calc(100%+0.75rem)] z-10 w-88"
            [style.top.px]="profileTop()"
            [member]="member"
            [bot]="member.botId ? (details().get(member.botId) ?? null) : null"
            [currentUserId]="currentUserId()"
            [canRemove]="canRemove(member)"
            (close)="profileId.set(null)"
            (mention)="mentionMember(member)"
            (toggleMute)="toggleMute(member)"
            (edit)="editBot(member, false)"
            (updateModel)="editBot(member, true)"
            (remove)="removeMember(member)"
          />
        }
      </hlm-sheet-content>
    </hlm-sheet>

    <!-- On phones the sheet is too narrow for a card beside it: the profile slides up full-width over it. -->
    <hlm-sheet side="bottom" [state]="!desktop() && profileMember() ? 'open' : 'closed'" (closed)="profileId.set(null)">
      <hlm-sheet-content
        *hlmSheetPortal="let ctx"
        [showCloseButton]="false"
        class="max-h-[85dvh] gap-0 overflow-y-auto rounded-t-xl p-0 pb-[env(safe-area-inset-bottom)]"
      >
        @if (profileMember(); as member) {
          <h2 hlmSheetTitle class="sr-only">{{ member.displayName }}</h2>
          <app-member-profile
            [inSheet]="true"
            [member]="member"
            [bot]="member.botId ? (details().get(member.botId) ?? null) : null"
            [currentUserId]="currentUserId()"
            [canRemove]="canRemove(member)"
            (close)="ctx.close()"
            (mention)="mentionMember(member)"
            (toggleMute)="toggleMute(member)"
            (edit)="editBot(member, false)"
            (updateModel)="editBot(member, true)"
            (remove)="removeMember(member)"
          />
        }
      </hlm-sheet-content>
    </hlm-sheet>

    <ng-template #menu let-member>
      <hlm-dropdown-menu class="w-48">
        <button hlmDropdownMenuItem (triggered)="mentionMember(member)">
          <ng-icon name="lucideAtSign" />
          Mention
        </button>
        @if (member.memberType === 'bot') {
          <button hlmDropdownMenuItem (triggered)="toggleMute(member)">
            <ng-icon [name]="member.isMuted ? 'lucideVolume2' : 'lucideVolumeX'" />
            {{ member.isMuted ? 'Unmute' : 'Mute' }}
          </button>
          @if (canEdit(member)) {
            <button hlmDropdownMenuItem (triggered)="editBot(member, broken(member))">
              <ng-icon name="lucidePencil" />
              {{ broken(member) ? 'Update model' : 'Edit bot' }}
            </button>
          }
        }
        @if (canRemove(member)) {
          <hlm-dropdown-menu-separator />
          <button hlmDropdownMenuItem variant="destructive" (triggered)="removeMember(member)">
            <ng-icon [name]="member.userId === currentUserId() ? 'lucideLogOut' : 'lucideUserMinus'" />
            {{ member.userId === currentUserId() ? 'Leave chat' : 'Remove from chat' }}
          </button>
        }
      </hlm-dropdown-menu>
    </ng-template>

    <app-bot-dialog
      [open]="dialogOpen()"
      [bot]="dialogBot()"
      [chatId]="chatId()"
      [focusModel]="dialogFocusModel()"
      (closed)="dialogOpen.set(false)"
      (saved)="onBotSaved($event.id)"
    />
  `,
})
export class PeopleSheet implements OnDestroy {
  private readonly directory = inject(BotDirectory);
  private readonly botsApi = inject(BotsService);
  private readonly chatBotsApi = inject(ChatBotsService);
  private readonly membersApi = inject(ChatMembersService);
  private readonly usersApi = inject(UserService);

  readonly open = input(false);
  readonly chatId = input.required<string>();
  readonly members = input.required<ChatMember[]>();
  readonly currentUserId = input<string | null>(null);

  readonly closed = output();
  /** Put `@name` in the composer. */
  readonly mention = output<string>();
  /** A local change to one member before the server confirms it (mute). */
  readonly patchMember = output<{ id: string; changes: Partial<ChatMember> }>();
  /** Members may have changed server-side (a bot's model was fixed); reload them. */
  readonly refresh = output();

  protected readonly desktop = mediaQuery('(min-width: 640px)');
  private readonly panel = viewChild<ElementRef<HTMLElement>>('panel');

  protected readonly pickClass =
    'hover:bg-muted focus-visible:ring-ring/50 flex w-full items-center gap-2.5 rounded-md px-2 py-1.5 text-left outline-none focus-visible:ring-2';

  /** Bot details (personality, stats, creator) by bot id; members only carry a summary. */
  protected readonly details = signal<ReadonlyMap<string, BotDto>>(new Map());
  protected readonly profileId = signal<string | null>(null);
  protected readonly profileTop = signal(0);

  protected readonly addOpen = signal(false);
  protected readonly addQuery = signal('');
  protected readonly invitable = signal<UserDto[]>([]);
  private inviteTimer?: ReturnType<typeof setTimeout>;

  protected readonly dialogOpen = signal(false);
  protected readonly dialogBot = signal<BotDto | null>(null);
  protected readonly dialogFocusModel = signal(false);

  protected readonly bots = computed(() => this.members().filter((m) => m.memberType === 'bot'));
  protected readonly people = computed(() => this.members().filter((m) => m.memberType === 'user'));
  protected readonly groups = computed(() => [
    { label: 'Bots', members: this.bots() },
    { label: 'People', members: this.people() },
  ]);
  protected readonly profileMember = computed(
    () => this.members().find((m) => m.id === this.profileId()) ?? null,
  );

  protected readonly addableBots = computed(() => {
    const inChat = new Set(this.bots().map((m) => m.botId));
    const query = this.addQuery().trim().toLowerCase();
    return this.directory
      .bots()
      .filter(
        (b) =>
          !inChat.has(b.id) &&
          (!query || b.name.toLowerCase().includes(query) || (b.personality ?? '').toLowerCase().includes(query)),
      );
  });

  constructor() {
    // Load the details of every bot in the chat while the sheet is open.
    effect(() => {
      if (!this.open()) return;
      const ids = this.bots().map((m) => m.botId!);
      untracked(() => void this.loadDetails(ids.filter((id) => !this.details().has(id))));
    });
    effect(() => {
      if (this.open()) void this.directory.load();
    });
    // People to invite: re-query when the search changes (debounced) or someone joins or leaves.
    effect(() => {
      const open = this.addOpen();
      const chatId = this.chatId();
      const search = this.addQuery().trim();
      this.members();
      clearTimeout(this.inviteTimer);
      if (!open || !chatId) return;
      this.inviteTimer = setTimeout(() => void this.loadInvitable(chatId, search), 200);
    });
    // Another chat: forget this one's cards.
    effect(() => {
      this.chatId();
      untracked(() => {
        this.profileId.set(null);
        this.details.set(new Map());
      });
    });
  }

  ngOnDestroy(): void {
    clearTimeout(this.inviteTimer);
  }

  protected onSheetClosed(): void {
    this.profileId.set(null);
    this.addOpen.set(false);
    this.closed.emit();
  }

  protected lineFor(member: ChatMember): string {
    if (member.memberType === 'bot') {
      return this.details().get(member.botId!)?.personality?.split('\n')[0] || 'Bot';
    }
    return member.userId === this.currentUserId() ? 'You' : joinedLabel(member.joinedAt);
  }

  protected isPrivate(member: ChatMember): boolean {
    const bot = member.botId ? this.details().get(member.botId) : null;
    return !!bot && !bot.isPublic;
  }

  protected broken(member: ChatMember): boolean {
    return hasModelProblem(member.modelStatus);
  }

  protected problemLabel(member: ChatMember): string {
    return modelProblemLabel(member.modelStatus);
  }

  protected canRemove(member: ChatMember): boolean {
    return canRemoveMember(member, this.members(), this.currentUserId());
  }

  protected canEdit(member: ChatMember): boolean {
    const bot = member.botId ? this.details().get(member.botId) : null;
    return !!bot && bot.createdById === this.currentUserId();
  }

  protected toggleProfile(member: ChatMember, event: MouseEvent): void {
    if (this.profileId() === member.id) {
      this.profileId.set(null);
      return;
    }
    const panel = this.panel()?.nativeElement.getBoundingClientRect();
    const row = (event.currentTarget as HTMLElement).getBoundingClientRect();
    const top = panel ? row.top - panel.top : 0;
    this.profileTop.set(Math.max(12, Math.min(top, window.innerHeight - PROFILE_HEIGHT - 12)));
    this.profileId.set(member.id);
    // Always refetch: the card shows live stats (replies today), and the cached copy is
    // from whenever the sheet first opened. The cached one shows until the fresh one lands.
    if (member.botId) void this.loadDetails([member.botId]);
  }

  protected mentionMember(member: ChatMember): void {
    this.mention.emit(member.displayName);
    this.profileId.set(null);
  }

  protected async toggleMute(member: ChatMember): Promise<void> {
    if (!member.botId) return;
    const muted = !member.isMuted;
    this.patchMember.emit({ id: member.id, changes: { isMuted: muted } });
    try {
      await firstValueFrom(
        muted
          ? this.chatBotsApi.apiChatsChatIdBotsBotIdMutePost(this.chatId(), member.botId)
          : this.chatBotsApi.apiChatsChatIdBotsBotIdUnmutePost(this.chatId(), member.botId),
      );
    } catch (error) {
      this.patchMember.emit({ id: member.id, changes: { isMuted: !muted } });
      toast.error(describeApiError(error, { fallback: `Could not ${muted ? 'mute' : 'unmute'} ${member.displayName}.` }));
    }
  }

  protected async editBot(member: ChatMember, focusModel: boolean): Promise<void> {
    if (!member.botId) return;
    let bot = this.details().get(member.botId);
    if (!bot) {
      await this.loadDetails([member.botId]);
      bot = this.details().get(member.botId);
    }
    if (!bot) return;
    this.dialogBot.set(bot);
    this.dialogFocusModel.set(focusModel);
    this.dialogOpen.set(true);
  }

  protected newBot(): void {
    this.dialogBot.set(null);
    this.dialogFocusModel.set(false);
    this.dialogOpen.set(true);
  }

  protected async onBotSaved(id: string): Promise<void> {
    await this.loadDetails([id]);
    void this.directory.reload();
    this.refresh.emit();
  }

  protected async removeMember(member: ChatMember): Promise<void> {
    if (this.profileId() === member.id) this.profileId.set(null);
    try {
      await firstValueFrom(this.membersApi.apiChatsChatIdMembersMemberIdDelete(this.chatId(), member.id));
    } catch (error) {
      toast.error(describeApiError(error, { fallback: `Could not remove ${member.displayName}.` }));
    }
  }

  protected async addBot(bot: BotDto): Promise<void> {
    try {
      await firstValueFrom(this.membersApi.apiChatsChatIdMembersPost(this.chatId(), { botId: bot.id }));
    } catch (error) {
      toast.error(describeApiError(error, { fallback: `Could not add ${bot.name}.` }));
    }
  }

  protected async invite(user: UserDto): Promise<void> {
    try {
      await firstValueFrom(this.membersApi.apiChatsChatIdMembersPost(this.chatId(), { userId: user.id }));
      this.invitable.update((list) => list.filter((u) => u.id !== user.id));
    } catch (error) {
      toast.error(describeApiError(error, { fallback: 'Could not invite that person.' }));
    }
  }

  private async loadDetails(ids: readonly string[]): Promise<void> {
    if (!ids.length) return;
    const loaded = await Promise.all(
      ids.map((id) => firstValueFrom(this.botsApi.apiBotsBotIdGet(id)).catch(() => null)),
    );
    this.details.update((map) => {
      const next = new Map(map);
      for (const bot of loaded) if (bot) next.set(bot.id, bot);
      return next;
    });
  }

  private async loadInvitable(chatId: string, search: string): Promise<void> {
    try {
      this.invitable.set(await firstValueFrom(this.usersApi.getUsers(search || undefined, chatId)));
    } catch {
      this.invitable.set([]);
    }
  }
}
