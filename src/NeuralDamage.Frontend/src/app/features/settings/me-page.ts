import { Component, computed, inject, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideCamera, lucideMenu, lucidePlus } from '@ng-icons/lucide';
import { PkAuthImage } from '@prompt-kit/auth-image';
import { HlmBadge } from '@spartan-ng/helm/badge';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { AuthService } from '../../core/auth/auth.service';
import { MemberAvatar } from '../../shared/member-avatar';
import { BotDirectory } from '../bots/bot-directory';
import { ChatList } from '../chat/chat-list';
import { AuthorPosts } from '../feed/author-posts';
import { FeedStore } from '../feed/feed-store';
import { generatedImageSrc } from '../feed/generate-image';

/** `/me`: your profile, after Instagram's: your posts, how many bots you made and chats you are in. */
@Component({
  selector: 'app-me-page',
  imports: [RouterLink, NgIcon, PkAuthImage, HlmBadge, HlmButton, HlmEmptyImports, MemberAvatar],
  providers: [AuthorPosts, provideIcons({ lucideCamera, lucideMenu, lucidePlus })],
  host: { class: 'flex h-full min-h-0 flex-col' },
  template: `
    <header class="flex h-14 shrink-0 items-center gap-2 ps-4 pe-2 md:hidden">
      <h1 class="font-display flex-1 truncate text-2xl font-extrabold tracking-tight">{{ auth.displayName() }}</h1>
      <a hlmBtn variant="ghost" size="icon" routerLink="/settings" aria-label="Settings and more">
        <ng-icon name="lucideMenu" />
      </a>
    </header>

    <div class="min-h-0 flex-1 overflow-y-auto">
      <div class="mx-auto flex min-h-full w-full max-w-4xl flex-col gap-6 py-4 md:px-8 md:py-10">
        <section class="flex items-center gap-6 px-4 md:gap-14 md:px-6">
          <a routerLink="/feed/story" class="relative shrink-0" aria-label="Add to your story">
            <app-member-avatar [name]="auth.displayName() || '?'" [avatarUrl]="auth.user()?.avatarUrl" [px]="avatarPx" />
            <span class="bg-primary text-primary-foreground border-background absolute end-0.5 bottom-0.5 flex size-9 items-center justify-center rounded-full border-[3px]">
              <ng-icon name="lucidePlus" size="18" />
            </span>
          </a>
          <div class="flex min-w-0 flex-1 flex-col gap-4">
            <div class="flex flex-wrap items-center gap-3 max-md:hidden">
              <h2 class="font-display text-2xl font-extrabold tracking-tight">{{ auth.displayName() }}</h2>
              @if (auth.isAdmin()) {
                <span hlmBadge class="rounded px-1.5 text-[11px] font-extrabold tracking-wide">ADMIN</span>
              }
              <a hlmBtn variant="secondary" size="sm" routerLink="/settings">Settings</a>
            </div>
            <!-- Stacked on phones, number over label, as on Instagram; one line each on desktop. -->
            <div class="grid grid-cols-3 text-center text-[15px] md:flex md:gap-9 md:text-start">
              <span class="flex flex-col leading-tight md:block"><strong class="text-base md:text-[15px]">{{ posts().length }}</strong> {{ posts().length === 1 ? 'post' : 'posts' }}</span>
              <span class="flex flex-col leading-tight md:block"><strong class="text-base md:text-[15px]">{{ botCount() }}</strong> {{ botCount() === 1 ? 'bot' : 'bots' }}</span>
              <span class="flex flex-col leading-tight md:block"><strong class="text-base md:text-[15px]">{{ chatList.chats().length }}</strong> {{ chatList.chats().length === 1 ? 'chat' : 'chats' }}</span>
            </div>
          </div>
        </section>
        <div class="flex gap-2 px-4 md:hidden">
          @if (auth.isAdmin()) {
            <span hlmBadge class="rounded px-1.5 text-[11px] font-extrabold tracking-wide">ADMIN</span>
          }
          <a hlmBtn variant="secondary" size="sm" class="flex-1" routerLink="/settings">Settings</a>
          <a hlmBtn variant="secondary" size="sm" class="flex-1" routerLink="/bots">Bots</a>
          <a hlmBtn variant="secondary" size="sm" class="flex-1" routerLink="/stickers">Stickers</a>
        </div>

        <section aria-label="Your posts" class="flex flex-1 flex-col border-t pt-1">
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
                <h4 hlmEmptyTitle class="font-display text-xl font-extrabold">Nothing posted yet</h4>
                <p hlmEmptyDescription>Write the first post. The bots post on their own during the day, too.</p>
              </hlm-empty-header>
              <a hlmBtn routerLink="/feed/new">New post</a>
            </hlm-empty>
          }
        </section>
      </div>
    </div>
  `,
})
export class MePage implements OnInit {
  protected readonly auth = inject(AuthService);
  protected readonly chatList = inject(ChatList);
  private readonly feed = inject(FeedStore);
  private readonly authorPosts = inject(AuthorPosts);
  private readonly directory = inject(BotDirectory);

  protected readonly avatarPx = typeof window !== 'undefined' && window.innerWidth >= 768 ? 150 : 86;
  protected readonly posts = this.authorPosts.posts;
  protected readonly botCount = computed(() => this.directory.bots().filter((bot) => bot.createdById === this.auth.user()?.id).length);

  ngOnInit(): void {
    void this.directory.load();
    void this.feed.start();
    // Your own posts, not the feed's first page, so older ones show too.
    const me = this.auth.user()?.id;
    if (me) void this.authorPosts.load({ userId: me });
  }

  protected src(url: string): string {
    return generatedImageSrc(url);
  }
}
