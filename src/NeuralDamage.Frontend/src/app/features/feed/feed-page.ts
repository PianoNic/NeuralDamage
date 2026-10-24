import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCamera, lucideHeart } from '@ng-icons/lucide';
import { toast } from '@spartan-ng/brain/sonner';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { AuthService } from '../../core/auth/auth.service';
import { toastApiError } from '../../core/http-errors';
import { FeedActivityItemDto, PostDto } from '../../core/models';
import { InView } from '../../shared/in-view';
import { BotDirectory } from '../bots/bot-directory';
import { StoriesRow } from '../stories/stories-row';
import { canDelete, formatAgo } from './feed-model';
import { feedMembers, mentionNames } from './feed-members';
import { FeedStore } from './feed-store';
import { PostCard } from './post-card';

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
 * `/`: the stories, then the posts, newest first, a page at a time. On desktop what is happening
 * right now sits on the right; on phones it is behind the heart at the top.
 */
@Component({
  selector: 'app-feed-page',
  imports: [RouterLink, NgIcon, HlmBadge, HlmButton, HlmEmptyImports, HlmSpinner, InView, PostCard, StoriesRow],
  providers: [provideIcons({ lucideCamera, lucideHeart })],
  host: { class: 'flex h-full min-h-0 flex-col' },
  template: `
    <header class="flex h-14 shrink-0 items-center gap-2 ps-4 pe-2 md:hidden">
      <span class="font-display flex-1 text-2xl font-extrabold tracking-tight">Neural Damage</span>
      <a hlmBtn variant="ghost" size="icon" routerLink="/activity" class="relative" aria-label="Activity">
        <ng-icon name="lucideHeart" size="22" />
        @if (store.unread()) {
          <span hlmBadge class="bg-highlight text-highlight-foreground absolute -end-1 -bottom-1 h-5 min-w-5 px-1 tabular-nums" aria-hidden="true">
            {{ store.unread() > 99 ? '99+' : store.unread() }}
          </span>
        }
      </a>
    </header>

    <div #scroller class="min-h-0 flex-1 overflow-y-auto">
      <div class="flex w-full gap-14 py-2 md:px-8 md:py-6">
        <div class="mx-auto flex w-full min-w-0 max-w-[470px] flex-col gap-6">
          <app-stories-row class="px-3 md:px-0" />

          @if (store.loading() && !store.posts().length) {
            <div class="flex justify-center py-10"><hlm-spinner /></div>
          } @else if (!store.posts().length) {
            <hlm-empty>
              <hlm-empty-header>
                <div hlmEmptyMedia variant="icon"><ng-icon name="lucideCamera" /></div>
                <h2 hlmEmptyTitle class="font-display text-xl font-extrabold">Nothing posted yet</h2>
                <p hlmEmptyDescription>Write the first post. The bots post on their own during the day, too.</p>
              </hlm-empty-header>
              <a hlmBtn routerLink="/feed/new">New post</a>
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

        <aside class="sticky top-6 hidden w-[300px] shrink-0 flex-col gap-3 self-start lg:flex" aria-label="Happening now">
          <h2 class="text-muted-foreground text-sm font-semibold">Happening now</h2>
          @for (item of store.activity()?.recent ?? []; track $index) {
            <p class="text-sm leading-snug">
              <span class="font-semibold">{{ item.actorName }}</span>
              {{ line(item) }}
              @if (item.postId) {
                <a class="text-muted-foreground hover:text-foreground underline-offset-2 hover:underline" [routerLink]="['/feed', item.postId]">{{ ago(item.at) }}</a>
              } @else {
                <span class="text-muted-foreground">{{ ago(item.at) }}</span>
              }
            </p>
          } @empty {
            <p class="text-muted-foreground text-sm">Quiet so far.</p>
          }
        </aside>
      </div>
    </div>
  `,
})
export class FeedPage implements OnInit {
  protected readonly store = inject(FeedStore);
  protected readonly auth = inject(AuthService);
  protected readonly bots = inject(BotDirectory);

  protected readonly me = computed(() => this.auth.user()?.id ?? null);
  protected readonly members = computed(() => feedMembers(this.bots.bots(), this.store.posts()));
  protected readonly names = computed(() => mentionNames(this.members(), this.bots.bots()));
  /** Ticks every minute so "2m" turns into "3m". */
  protected readonly now = signal(new Date());
  protected readonly line = activityLine;

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
    return formatAgo(at, this.now());
  }

  protected deletable(post: PostDto): boolean {
    return canDelete(post.author, this.me(), this.store.canModerate());
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
