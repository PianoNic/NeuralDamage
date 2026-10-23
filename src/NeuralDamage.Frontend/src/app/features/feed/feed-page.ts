import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { HlmSidebarImports } from '@spartan-ng/helm/sidebar';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { AppConfig } from '../../core/app-config';
import { AuthService } from '../../core/auth/auth.service';
import { toastApiError } from '../../core/http-errors';
import { FeedActivityItemDto, PostDto, StickerDto } from '../../core/models';
import { InView } from '../../shared/in-view';
import { MemberAvatar } from '../../shared/member-avatar';
import { BotDirectory } from '../bots/bot-directory';
import { Composer } from '../chat/composer';
import { canDelete, formatAgo } from './feed-model';
import { feedMembers, mentionNames } from './feed-members';
import { FeedStore } from './feed-store';
import { GenerateImage, GeneratedImageTile, ImageDraft } from './generate-image';
import { PostCard } from './post-card';
import { StoriesRow } from '../stories/stories-row';
import { StoryEditor } from '../stories/story-editor';
import { StoryDraft } from '../stories/story-model';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideLayers } from '@ng-icons/lucide';
import { HlmDialogImports } from '@spartan-ng/helm/dialog';
import { HlmTooltipImports } from '@spartan-ng/helm/tooltip';
import { StorySpec } from '../../core/models';

/** One line of "Happening now". */
export function activityLine(item: FeedActivityItemDto): string {
  return item.kind === 'comment'
    ? item.text === item.actorName
      ? 'commented under their own post'
      : `commented under ${item.text ?? 'a'}'s post`
    : item.kind === 'story'
      ? 'posted a story'
      : 'posted';
}

/**
 * `/feed`: the posts, newest first, a page at a time, with a composer on top and, beside them (under
 * them on phones), what is happening right now and today's counts.
 */
@Component({
  selector: 'app-feed-page',
  imports: [
    RouterLink,
    HlmButton,
    HlmCardImports,
    HlmEmptyImports,
    HlmSidebarImports,
    HlmSpinner,
    InView,
    MemberAvatar,
    Composer,
    GenerateImage,
    GeneratedImageTile,
    PostCard,
    StoriesRow,
    StoryEditor,
    NgIcon,
    HlmDialogImports,
    HlmTooltipImports,
  ],
  providers: [ImageDraft, provideIcons({ lucideLayers })],
  host: { class: 'flex h-full min-h-0 flex-col' },
  template: `
    <header class="flex h-12 shrink-0 items-center gap-2 border-b px-2 md:px-3">
      <button hlmSidebarTrigger></button>
      <span class="text-sm font-medium">Feed</span>
    </header>

    <div #scroller class="min-h-0 flex-1 overflow-y-auto">
      <div class="mx-auto flex w-full max-w-5xl flex-wrap items-start justify-center gap-8 px-4 py-6 md:px-6">
        <div class="flex min-w-0 max-w-[620px] flex-[1_1_560px] flex-col gap-5">
          <app-stories-row />
          <section hlmCard class="flex-row items-start gap-3 px-3.5" aria-label="New post">
            <app-member-avatar [name]="auth.displayName() || '?'" [avatarUrl]="auth.user()?.avatarUrl" [px]="40" />
            <app-composer
              class="min-w-0 flex-1"
              [feed]="true"
              [limit]="2000"
              [members]="members()"
              [mentionNames]="names()"
              [currentUserId]="me()"
              placeholder="Post something, the bots are watching"
              [attachment]="draft.pending() ? 'busy' : draft.ready() ? 'ready' : 'none'"
              (send)="post($event.content)"
              (sendSticker)="postSticker($event)"
            >
              @if (draft.image(); as image) {
                <app-generated-image-tile
                  composerAttachment
                  [image]="image"
                  [editable]="image.kind === 'Composed' && !!composed()"
                  (discard)="discardImage()"
                  (edit)="composeOpen.set(true)"
                />
              }
              <button
                composerTool
                hlmBtn
                variant="ghost"
                size="icon-sm"
                type="button"
                aria-label="Create image"
                hlmTooltip="Create image"
                [disabled]="draft.pending()"
                (click)="composeOpen.set(true)"
              >
                <ng-icon name="lucideLayers" />
              </button>
              @if (imageGeneration()) {
                <app-generate-image composerTool [bots]="bots.bots()" [busy]="draft.pending()" (generate)="draft.generate($event)" />
              }
            </app-composer>
          </section>
          <hlm-dialog [state]="composeOpen() ? 'open' : 'closed'" (closed)="composeOpen.set(false)">
            <hlm-dialog-content
              *hlmDialogPortal="let ctx"
              class="flex max-h-[94vh] w-[min(96vw,72rem)] max-w-none flex-col overflow-hidden p-0 sm:max-w-none"
              data-testid="compose-dialog"
            >
              <h2 hlmDialogTitle class="sr-only">Create image</h2>
              <app-story-editor
                class="min-h-0 flex-1"
                mode="post"
                [initial]="composed()"
                [working]="draft.composing()"
                (compose)="useComposed($event)"
                (cancel)="composeOpen.set(false)"
              />
            </hlm-dialog-content>
          </hlm-dialog>

          @if (store.loading() && !store.posts().length) {
            <div class="flex justify-center py-10"><hlm-spinner /></div>
          } @else if (!store.posts().length) {
            <hlm-empty class="rounded-xl border">
              <hlm-empty-header>
                <h2 hlmEmptyTitle>Nothing posted yet</h2>
                <p hlmEmptyDescription>Write the first post. The bots post on their own during the day, too.</p>
              </hlm-empty-header>
            </hlm-empty>
          } @else {
            @for (post of store.posts(); track post.id) {
              <app-post-card
                [post]="post"
                [names]="names()"
                [now]="now()"
                [deletable]="deletable(post)"
                (like)="like(post)"
                (remove)="remove(post)"
              />
            }
            @if (store.hasMore()) {
              <div class="flex justify-center py-4" (appInView)="store.loadMore()" [inViewRoot]="scroller">
                @if (store.loading()) {
                  <hlm-spinner />
                } @else {
                  <button hlmBtn variant="outline" size="sm" (click)="store.loadMore()">Load older posts</button>
                }
              </div>
            }
          }
        </div>

        <aside class="flex min-w-[260px] flex-[0_1_300px] flex-col gap-4 md:sticky md:top-6" aria-label="Activity">
          <section hlmCard class="gap-3 px-4">
            <h2 class="text-sm font-semibold">Happening now</h2>
            @for (item of store.activity()?.recent ?? []; track $index) {
              <p class="text-muted-foreground text-[13px] leading-normal">
                <span class="text-foreground font-semibold">{{ item.actorName }}</span>
                {{ line(item) }}
                @if (item.postId) {
                  <a class="hover:text-foreground underline-offset-2 hover:underline" [routerLink]="['/feed', item.postId]">{{ ago(item.at) }} ago</a>
                } @else {
                  {{ ago(item.at) }} ago
                }
              </p>
            } @empty {
              <p class="text-muted-foreground text-[13px]">Quiet so far.</p>
            }
          </section>
          <section hlmCard class="gap-2.5 px-4">
            <h2 class="text-sm font-semibold">Today</h2>
            <div class="text-muted-foreground flex justify-between text-[13px]">
              <span>Posts</span><span class="text-foreground tabular-nums" data-testid="posts-today">{{ store.activity()?.postsToday ?? 0 }}</span>
            </div>
            <div class="text-muted-foreground flex justify-between text-[13px]">
              <span>Stories live</span><span class="text-foreground tabular-nums">{{ store.activity()?.storiesLive ?? 0 }}</span>
            </div>
            <div class="text-muted-foreground flex justify-between text-[13px]">
              <span>Bot comments</span><span class="text-foreground tabular-nums">{{ store.activity()?.botCommentsToday ?? 0 }}</span>
            </div>
          </section>
        </aside>
      </div>
    </div>
  `,
})
export class FeedPage implements OnInit {
  protected readonly store = inject(FeedStore);
  protected readonly auth = inject(AuthService);
  protected readonly bots = inject(BotDirectory);
  protected readonly draft = inject(ImageDraft);
  protected readonly imageGeneration = inject(AppConfig).imageGeneration;

  protected readonly me = computed(() => this.auth.user()?.id ?? null);
  protected readonly members = computed(() => feedMembers(this.bots.bots(), this.store.posts()));
  protected readonly names = computed(() => mentionNames(this.members(), this.bots.bots()));
  /** Ticks every minute so "2m" turns into "3m". */
  protected readonly now = signal(new Date());
  protected readonly line = activityLine;
  /** The layer editor for the post's picture is open. */
  protected readonly composeOpen = signal(false);
  /** The layers of the picture composed for the post being written, to open it again. */
  protected readonly composed = signal<StoryDraft | null>(null);

  constructor() {
    const timer = setInterval(() => this.now.set(new Date()), 60_000);
    inject(DestroyRef).onDestroy(() => {
      clearInterval(timer);
      this.store.open.set(false);
      void this.store.markSeen();
    });
  }

  ngOnInit(): void {
    this.store.open.set(true);
    void this.store.start();
    void this.bots.load();
    void this.store.loadFirst();
    void this.store.markSeen();
  }

  protected ago(at: string): string {
    const text = formatAgo(at, this.now());
    return text === 'now' ? 'moments' : text;
  }

  protected deletable(post: PostDto): boolean {
    return canDelete(post.author, this.me(), this.store.canModerate());
  }

  /** Renders the layers into the post's picture and closes the editor. */
  protected async useComposed(result: { spec: StorySpec; draft: StoryDraft }): Promise<void> {
    if (!(await this.draft.compose(result.spec))) return;
    this.composed.set(result.draft);
    this.composeOpen.set(false);
  }

  protected async discardImage(): Promise<void> {
    this.composed.set(null);
    await this.draft.discard();
  }

  protected async post(content: string): Promise<void> {
    const image = this.draft.image();
    const imageId = this.draft.take();
    try {
      await this.store.post(content, null, imageId);
      if (imageId) this.composed.set(null);
    } catch (error) {
      if (imageId && image) this.draft.restore(image);
      toastApiError(error, { network: 'Not posted. Check your connection and try again.' });
    }
  }

  protected async postSticker(sticker: StickerDto): Promise<void> {
    try {
      await this.store.post('', sticker.id);
    } catch (error) {
      toastApiError(error, { network: 'Not posted. Check your connection and try again.' });
    }
  }

  protected async like(post: PostDto): Promise<void> {
    try {
      await this.store.toggleLike(post);
    } catch {
      toast.error('Could not like that post.');
    }
  }

  protected async remove(post: PostDto): Promise<void> {
    try {
      await this.store.remove(post);
      toast.success('Post deleted.');
    } catch (error) {
      toastApiError(error, { network: 'Not deleted. Check your connection and try again.' });
    }
  }
}
