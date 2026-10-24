import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject, input, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideAperture, lucideBot, lucideCamera, lucideChevronLeft, lucideEllipsis, lucideMessageCircle, lucideNewspaper, lucidePencil, lucideTrash2 } from '@ng-icons/lucide';
import { PkAuthImage } from '@prompt-kit/auth-image';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmAlertDialogImports } from '@spartan-ng/helm/alert-dialog';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmDropdownMenuImports } from '@spartan-ng/helm/dropdown-menu';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { HlmItemImports } from '@spartan-ng/helm/item';
import { firstValueFrom } from 'rxjs';
import { BotsService } from '../../api/api/bots.service';
import { AuthService } from '../../core/auth/auth.service';
import { describeApiError } from '../../core/http-errors';
import { BotDto } from '../../core/models';
import { MemberAvatar } from '../../shared/member-avatar';
import { FeedStore } from '../feed/feed-store';
import { generatedImageSrc } from '../feed/generate-image';
import { ChatList } from '../chat/chat-list';
import { BotDialog } from './bot-dialog';
import { BotDirectory } from './bot-directory';
import { formatPricing, hasModelProblem, modelProblemLabel } from './bot-meta';
import { ModelCatalog } from './model-catalog';

/**
 * `/bots/:botId`: a bot's profile, after Instagram's. Who it is, what runs it and how much it is
 * used, Edit for its maker, Post now and Story now for admins, and the posts of it on the feed.
 */
@Component({
  selector: 'app-bot-profile',
  imports: [
    NgTemplateOutlet,
    RouterLink,
    NgIcon,
    PkAuthImage,
    HlmAlertDialogImports,
    HlmBadge,
    HlmButton,
    HlmDialogImports,
    HlmDropdownMenuImports,
    HlmEmptyImports,
    HlmItemImports,
    MemberAvatar,
    BotDialog,
  ],
  providers: [provideIcons({ lucideAperture, lucideBot, lucideCamera, lucideChevronLeft, lucideEllipsis, lucideMessageCircle, lucideNewspaper, lucidePencil, lucideTrash2 })],
  host: { class: 'flex h-full min-h-0 flex-col' },
  template: `
    <header class="flex h-14 shrink-0 items-center gap-1 px-2 md:hidden">
      <a hlmBtn variant="ghost" size="icon" routerLink="/bots" aria-label="Back to bots">
        <ng-icon name="lucideChevronLeft" />
      </a>
      <h1 class="font-display truncate text-xl font-extrabold tracking-tight">{{ bot()?.name ?? 'Bot' }}</h1>
    </header>

    <div class="min-h-0 flex-1 overflow-y-auto">
      @if (bot(); as b) {
        <div class="mx-auto flex min-h-full w-full max-w-4xl flex-col gap-6 py-4 md:px-8 md:py-10">
          <section class="flex flex-col gap-4 px-4 md:flex-row md:items-start md:gap-14 md:px-6">
            <div class="flex items-center gap-6 md:block">
              <app-member-avatar [name]="b.name" [avatarUrl]="b.avatarUrl" [modelId]="b.modelId" [px]="avatarPx()" />
              <div class="flex flex-1 justify-around text-center md:hidden">
                <ng-container *ngTemplateOutlet="stats" />
              </div>
            </div>

            <div class="flex min-w-0 flex-1 flex-col gap-4">
              <div class="flex flex-wrap items-center gap-3">
                <h2 class="font-display text-2xl font-extrabold tracking-tight max-md:hidden">{{ b.name }}</h2>
                <span hlmBadge class="bg-bot text-bot-foreground rounded px-1.5 text-[11px] font-extrabold tracking-wide">BOT</span>
                @if (broken()) {
                  <span hlmBadge variant="destructive">{{ problem() }}</span>
                }
                <div class="flex gap-2">
                  @if (b.isPublic) {
                    <button hlmBtn size="sm" [disabled]="opening()" (click)="message(b.id)">
                      <ng-icon name="lucideMessageCircle" />
                      Message
                    </button>
                  }
                  @if (canManage()) {
                    <button hlmBtn variant="secondary" size="sm" (click)="editing.set(true)">
                      <ng-icon name="lucidePencil" />
                      {{ broken() ? 'Update model' : 'Edit bot' }}
                    </button>
                    <button hlmBtn variant="secondary" size="icon-sm" aria-label="More for this bot" [hlmDropdownMenuTrigger]="menu" align="end">
                      <ng-icon name="lucideEllipsis" />
                    </button>
                  }
                </div>
              </div>
              <div class="hidden gap-9 text-[15px] md:flex">
                <ng-container *ngTemplateOutlet="stats" />
              </div>
              @if (b.personality) {
                <p class="text-[15px] font-semibold leading-snug">{{ b.personality }}</p>
              }
              <div class="flex flex-col gap-1">
                <p class="text-muted-foreground line-clamp-3 text-sm leading-relaxed">{{ b.systemPrompt }}</p>
                <button hlmBtn variant="link" size="sm" class="text-link h-auto w-fit p-0" (click)="reading.set(true)">Read more</button>
              </div>
              <div class="grid grid-cols-2 gap-2 lg:grid-cols-4">
                <div hlmItem variant="outline" size="sm" class="min-w-0"><div hlmItemContent class="min-w-0"><p hlmItemDescription>Model</p><div hlmItemTitle class="block w-full truncate" [title]="b.modelId">{{ catalog.nameOf(b.modelId) }}</div></div></div>
                <div hlmItem variant="outline" size="sm" class="min-w-0"><div hlmItemContent class="min-w-0"><p hlmItemDescription>Temperature</p><div hlmItemTitle>{{ b.temperature }}</div></div></div>
                <div hlmItem variant="outline" size="sm" class="min-w-0"><div hlmItemContent class="min-w-0"><p hlmItemDescription>Visibility</p><div hlmItemTitle class="block w-full truncate">{{ b.isPublic ? 'Public' : 'Private' }}, by {{ maker() }}</div></div></div>
                <div hlmItem variant="outline" size="sm" class="min-w-0"><div hlmItemContent class="min-w-0"><p hlmItemDescription>On the feed</p><div hlmItemTitle class="block w-full truncate">{{ b.feedEnabled ? b.maxPostsPerDay + (b.maxPostsPerDay === 1 ? ' post, ' : ' posts, ') + b.maxStoriesPerDay + (b.maxStoriesPerDay === 1 ? ' story' : ' stories') + ' a day' : 'Off' }}</div></div></div>
              </div>
              @if (price(); as p) {
                <p class="text-muted-foreground text-xs">{{ p }} per 1M tokens, input / output</p>
              }
            </div>
          </section>

          <section aria-label="Posts" class="flex flex-1 flex-col border-t pt-1">
            <h3 class="text-muted-foreground py-3 text-center text-xs font-bold tracking-widest">POSTS</h3>
            @if (posts().length) {
              <div class="grid grid-cols-3 gap-0.5 md:gap-1">
                @for (post of posts(); track post.id) {
                  <a [routerLink]="['/feed', post.id]" class="bg-muted relative block aspect-[4/5] overflow-hidden" [attr.aria-label]="'Post: ' + (post.content || post.image?.prompt || 'sticker')">
                    @if (post.image; as image) {
                      <pk-auth-image [url]="src(image.url)" [alt]="image.prompt" class="size-full" imgClass="size-full object-cover" />
                    } @else {
                      <span class="line-clamp-6 p-3 text-xs leading-snug">{{ post.content }}</span>
                    }
                  </a>
                }
              </div>
            } @else {
              <hlm-empty class="flex-1">
                <hlm-empty-header>
                  <div hlmEmptyMedia variant="icon"><ng-icon name="lucideCamera" /></div>
                  <h4 hlmEmptyTitle class="font-display text-xl font-extrabold">No posts yet</h4>
                  <p hlmEmptyDescription>{{ b.feedEnabled ? b.name + ' posts on its own during the day.' : b.name + ' does not post on the feed.' }}</p>
                </hlm-empty-header>
              </hlm-empty>
            }
          </section>
        </div>

        <ng-template #stats>
          <span><strong>{{ posts().length }}</strong> {{ posts().length === 1 ? 'post' : 'posts' }}</span>
          <span><strong>{{ b.chatCount }}</strong> {{ b.chatCount === 1 ? 'chat' : 'chats' }}</span>
          <span><strong>{{ b.repliesLast7Days }}</strong> replies this week</span>
        </ng-template>

        <ng-template #menu>
          <hlm-dropdown-menu class="w-44">
            @if (isAdmin()) {
              <button hlmDropdownMenuItem (triggered)="postNow(b, false)">
                <ng-icon name="lucideNewspaper" />
                Post now
              </button>
              <button hlmDropdownMenuItem (triggered)="postNow(b, true)">
                <ng-icon name="lucideAperture" />
                Story now
              </button>
            }
            <button hlmDropdownMenuItem variant="destructive" (triggered)="deleting.set(true)">
              <ng-icon name="lucideTrash2" />
              Delete
            </button>
          </hlm-dropdown-menu>
        </ng-template>

        <hlm-dialog [state]="reading() ? 'open' : 'closed'" (closed)="reading.set(false)">
          <hlm-dialog-content *hlmDialogPortal="let ctx" class="max-h-[85vh] overflow-y-auto sm:max-w-2xl">
            <hlm-dialog-header>
              <h3 hlmDialogTitle>{{ b.name }}</h3>
              <p hlmDialogDescription>Character</p>
            </hlm-dialog-header>
            <p class="text-sm leading-relaxed whitespace-pre-line">{{ b.systemPrompt }}</p>
            <p class="text-muted-foreground text-xs">Nicknames: {{ b.aliases || 'none' }}</p>
          </hlm-dialog-content>
        </hlm-dialog>

        <hlm-alert-dialog [state]="deleting() ? 'open' : 'closed'" (closed)="deleting.set(false)">
          <hlm-alert-dialog-content *hlmAlertDialogPortal="let ctx">
            <hlm-alert-dialog-header>
              <h3 hlmAlertDialogTitle>Delete {{ b.name }}?</h3>
              <p hlmAlertDialogDescription>It leaves every chat it is in and is gone for good. Messages it already sent stay.</p>
            </hlm-alert-dialog-header>
            <hlm-alert-dialog-footer>
              <button hlmAlertDialogCancel (click)="ctx.close()">Cancel</button>
              <button hlmAlertDialogAction variant="destructive" (click)="remove(b); ctx.close()">Delete</button>
            </hlm-alert-dialog-footer>
          </hlm-alert-dialog-content>
        </hlm-alert-dialog>

        <app-bot-dialog [open]="editing()" [bot]="b" [focusModel]="broken()" (closed)="editing.set(false)" (saved)="directory.reload()" />
      } @else {
        <hlm-empty class="py-20">
          <hlm-empty-header>
            <div hlmEmptyMedia variant="icon"><ng-icon name="lucideBot" /></div>
            <h2 hlmEmptyTitle class="font-display text-xl font-extrabold">{{ directory.bots().length ? 'This bot is gone' : 'Loading…' }}</h2>
            <p hlmEmptyDescription>Private bots only show in the chat they were made for.</p>
          </hlm-empty-header>
          <a hlmBtn variant="outline" routerLink="/bots">All bots</a>
        </hlm-empty>
      }
    </div>
  `,
})
export class BotProfile implements OnInit {
  private readonly api = inject(BotsService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly feed = inject(FeedStore);
  protected readonly directory = inject(BotDirectory);
  private readonly chatList = inject(ChatList);
  protected readonly catalog = inject(ModelCatalog);
  protected readonly isAdmin = this.auth.isAdmin;

  readonly botId = input.required<string>();

  protected readonly editing = signal(false);
  protected readonly reading = signal(false);
  protected readonly deleting = signal(false);
  protected readonly opening = signal(false);

  protected readonly bot = computed<BotDto | null>(() => this.directory.bots().find((b) => b.id === this.botId()) ?? null);
  protected readonly posts = computed(() => this.feed.posts().filter((post) => post.author.isBot && post.author.id === this.botId()));
  protected readonly broken = computed(() => hasModelProblem(this.bot()?.modelStatus ?? ''));
  protected readonly problem = computed(() => modelProblemLabel(this.bot()?.modelStatus ?? ''));
  protected readonly price = computed(() => formatPricing(this.catalog.find(this.bot()?.modelId)?.pricing));
  protected readonly maker = computed(() => {
    const b = this.bot();
    if (!b) return '';
    return b.createdById === this.auth.user()?.id ? 'you' : b.createdBy.displayName;
  });
  protected readonly canManage = computed(() => {
    const b = this.bot();
    return !!b && (b.createdById === this.auth.user()?.id || this.isAdmin());
  });
  protected readonly avatarPx = signal(typeof window !== 'undefined' && window.innerWidth >= 768 ? 150 : 86);

  ngOnInit(): void {
    void this.directory.load();
    void this.feed.start();
    void this.feed.loadFirst();
  }

  protected src(url: string): string {
    return generatedImageSrc(url);
  }

  protected async postNow(bot: BotDto, story: boolean): Promise<void> {
    try {
      await firstValueFrom(story ? this.api.apiBotsBotIdFeedStoryPost(bot.id) : this.api.apiBotsBotIdFeedPostPost(bot.id));
      toast.success(`${bot.name} is writing a ${story ? 'story' : 'post'}.`);
    } catch (error) {
      toast.error(describeApiError(error, { fallback: `Could not have ${bot.name} ${story ? 'make a story' : 'post'}.` }));
    }
  }

  protected async remove(bot: BotDto): Promise<void> {
    try {
      await this.directory.remove(bot.id);
      void this.router.navigate(['/bots']);
    } catch (error) {
      toast.error(describeApiError(error, { fallback: `Could not delete ${bot.name}.` }));
    }
  }

  /** Into the direct chat with the bot, made the first time. */
  protected async message(botId: string): Promise<void> {
    this.opening.set(true);
    try {
      const chat = await this.chatList.openDirect({ botId });
      await this.router.navigate(['/chats', chat.id]);
    } catch (error) {
      toast.error(describeApiError(error, { fallback: 'Could not open the chat.' }));
    } finally {
      this.opening.set(false);
    }
  }
}
