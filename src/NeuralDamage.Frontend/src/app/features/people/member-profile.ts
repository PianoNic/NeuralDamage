import { Component, computed, inject, input, output, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideAtSign,
  lucideLogOut,
  lucidePencil,
  lucideTriangleAlert,
  lucideUserMinus,
  lucideVolume2,
  lucideVolumeX,
  lucideX,
} from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { BotDto, ChatMember } from '../../core/models';
import { hasModelProblem } from '../bots/bot-meta';
import { ModelCatalog } from '../bots/model-catalog';
import { MemberSummary } from './member-summary';
import { joinedDate, roleLabel, visibilityLabel } from './people-meta';

/** About what fits on the subtitle line of the card. */
const SUBTITLE_CHARS = 40;
/** About what four lines of the card hold. */
const READ_MORE_CHARS = 200;

/**
 * Who a bot or person is, and what you can do with them: a card beside the People sheet on wider
 * screens, or the body of a full-width sheet over it on phones (`inSheet`).
 */
@Component({
  selector: 'app-member-profile',
  imports: [NgIcon, HlmButton, HlmDialogImports, HlmSpinner, MemberSummary],
  providers: [
    provideIcons({
      lucideAtSign,
      lucideLogOut,
      lucidePencil,
      lucideTriangleAlert,
      lucideUserMinus,
      lucideVolume2,
      lucideVolumeX,
      lucideX,
    }),
  ],
  host: {
    '[attr.role]': 'inSheet() ? null : "dialog"',
    '[attr.aria-label]': 'inSheet() ? null : member().displayName',
    class: 'text-popover-foreground flex flex-col gap-3.5 p-4 text-sm',
    '[class]': 'inSheet() ? "" : "bg-popover ring-foreground/10 rounded-xl shadow-xl ring-1"',
    '(keydown.escape)': 'close.emit()',
  },
  template: `
    <div class="flex items-center gap-3">
      <app-member-summary class="flex-1" [member]="member()" [bot]="bot()" [currentUserId]="currentUserId()" />
      <button hlmBtn variant="ghost" size="icon-sm" aria-label="Close profile" (click)="close.emit()">
        <ng-icon name="lucideX" />
      </button>
    </div>

    @if (isBot()) {
      @if (!bot()) {
        <div class="flex justify-center py-4"><hlm-spinner /></div>
      } @else {
        @if (description()) {
          <p class="text-[13px] leading-relaxed">{{ description() }}</p>
        }
        @if (character()) {
          <div class="flex flex-col items-start gap-1">
            <p class="text-muted-foreground line-clamp-4 text-[13px] leading-relaxed whitespace-pre-line">{{ character() }}</p>
            @if (characterIsLong()) {
              <button hlmBtn variant="link" size="sm" class="h-auto p-0 text-[13px]" (click)="reading.set(true)">Read more</button>
            }
          </div>
        }
        @if (nicknames()) {
          <p class="text-muted-foreground text-[13px]">Nicknames: {{ nicknames() }}.</p>
        }

        @if (broken()) {
          <div class="bg-destructive/10 text-destructive flex items-start gap-2 rounded-lg p-2.5 text-[13px]" role="status">
            <ng-icon name="lucideTriangleAlert" class="mt-0.5 shrink-0" />
            <span class="flex-1">{{ member().modelStatusReason || 'This bot\\'s model can no longer be used.' }} It stays quiet until the model is changed.</span>
          </div>
        }

        <dl class="grid grid-cols-2 gap-2.5 text-[13px]">
          <div class="flex min-w-0 flex-col gap-0.5">
            <dt class="text-muted-foreground text-xs">Model</dt>
            <dd class="truncate" [class.text-destructive]="broken()">{{ modelName() }}</dd>
          </div>
          <div class="flex flex-col gap-0.5">
            <dt class="text-muted-foreground text-xs">Replies today</dt>
            <dd class="tabular-nums">{{ bot()!.repliesToday }}</dd>
          </div>
          <div class="flex flex-col gap-0.5">
            <dt class="text-muted-foreground text-xs">Temperature</dt>
            <dd class="tabular-nums">{{ bot()!.temperature.toFixed(1) }}</dd>
          </div>
          <div class="flex min-w-0 flex-col gap-0.5">
            <dt class="text-muted-foreground text-xs">Visibility</dt>
            <dd>{{ visibility() }}</dd>
          </div>
        </dl>
      }
    } @else {
      <dl class="grid grid-cols-2 gap-2.5 text-[13px]">
        <div class="flex flex-col gap-0.5">
          <dt class="text-muted-foreground text-xs">Role</dt>
          <dd>{{ role() }}</dd>
        </div>
        <div class="flex flex-col gap-0.5">
          <dt class="text-muted-foreground text-xs">Joined</dt>
          <dd class="tabular-nums">{{ joined() }}</dd>
        </div>
      </dl>
    }

    <!-- Nothing to do with your own card but leave. -->
    @if (!isSelf()) {
      <div class="flex flex-wrap gap-2">
        <button hlmBtn variant="outline" size="sm" class="flex-1" (click)="mention.emit()">
          <ng-icon name="lucideAtSign" />
          Mention
        </button>
        @if (isBot()) {
          <button hlmBtn variant="outline" size="sm" class="flex-1" (click)="toggleMute.emit()">
            <ng-icon [name]="member().isMuted ? 'lucideVolume2' : 'lucideVolumeX'" />
            {{ member().isMuted ? 'Unmute' : 'Mute' }}
          </button>
          @if (canEdit()) {
            @if (broken()) {
              <button hlmBtn size="sm" class="flex-1" (click)="updateModel.emit()">
                <ng-icon name="lucidePencil" />
                Update model
              </button>
            } @else {
              <button hlmBtn variant="outline" size="sm" class="flex-1" (click)="edit.emit()">
                <ng-icon name="lucidePencil" />
                Edit
              </button>
            }
          }
        }
      </div>
    }

    @if (canRemove()) {
      <button hlmBtn variant="ghost" size="sm" class="text-destructive hover:text-destructive -mt-1 w-full" (click)="remove.emit()">
        <ng-icon [name]="isSelf() ? 'lucideLogOut' : 'lucideUserMinus'" />
        {{ isSelf() ? 'Leave chat' : 'Remove from chat' }}
      </button>
    }

    <hlm-dialog [state]="reading() ? 'open' : 'closed'" (closed)="reading.set(false)">
      <hlm-dialog-content *hlmDialogPortal="let ctx" class="sm:max-w-lg">
        <hlm-dialog-header>
          <h2 hlmDialogTitle>{{ member().displayName }}</h2>
          @if (bot()?.personality?.trim(); as short) {
            <p hlmDialogDescription>{{ short }}</p>
          }
        </hlm-dialog-header>
        <div class="max-h-[60vh] overflow-y-auto text-sm leading-relaxed whitespace-pre-line">{{ character() }}</div>
        @if (nicknames()) {
          <p class="text-muted-foreground text-[13px]">Nicknames: {{ nicknames() }}.</p>
        }
      </hlm-dialog-content>
    </hlm-dialog>
  `,
})
export class MemberProfile {
  private readonly catalog = inject(ModelCatalog);

  readonly member = input.required<ChatMember>();
  /** The bot's details, once loaded; null for people. */
  readonly bot = input<BotDto | null>(null);
  readonly currentUserId = input<string | null>(null);
  /** Whether the viewer is an admin, who may edit anyone's bot. */
  readonly isAdmin = input(false);
  /** Whether the viewer may remove this member (or leave, for themselves). */
  readonly canRemove = input(false);
  /** Shown as the body of a sheet: no card chrome, and the sheet is the dialog. */
  readonly inSheet = input(false);

  readonly close = output();
  readonly mention = output();
  readonly toggleMute = output();
  readonly edit = output();
  readonly updateModel = output();
  readonly remove = output();

  protected readonly isBot = computed(() => this.member().memberType === 'bot');
  protected readonly isSelf = computed(() => !!this.member().userId && this.member().userId === this.currentUserId());
  protected readonly role = computed(() => roleLabel(this.member().role));
  protected readonly joined = computed(() => joinedDate(this.member().joinedAt));
  protected readonly broken = computed(() => hasModelProblem(this.member().modelStatus));
  protected readonly canEdit = computed(() => {
    const bot = this.bot();
    return !!bot && (bot.createdById === this.currentUserId() || this.isAdmin());
  });

  /** Whether the full character is open in its own dialog. */
  protected readonly reading = signal(false);

  /** The short description, in full when the subtitle line had to cut it. */
  protected readonly description = computed(() => {
    const personality = this.bot()?.personality?.trim() ?? '';
    return personality.includes('\n') || personality.length > SUBTITLE_CHARS ? personality : '';
  });
  protected readonly character = computed(() => this.bot()?.systemPrompt?.trim() ?? '');
  /** Past about four lines of the card the character is cut, and "Read more" shows the rest. */
  protected readonly characterIsLong = computed(() => this.character().length > READ_MORE_CHARS);
  protected readonly nicknames = computed(() => this.bot()?.aliases?.trim() ?? '');

  protected readonly modelName = computed(() => this.catalog.nameOf(this.bot()?.modelId ?? this.member().modelId));
  protected readonly visibility = computed(() => {
    const bot = this.bot();
    return bot ? visibilityLabel(bot, this.currentUserId()) : '';
  });
}
