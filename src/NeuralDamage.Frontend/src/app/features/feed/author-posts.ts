import { computed, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { FeedService } from '../../api/api/feed.service';
import { PostDto } from '../../core/models';
import { FeedStore } from './feed-store';

/** As many as the API gives in one page; a profile's grid shows the newest of them. */
const PAGE = 50;

export type PostAuthor = { botId: string } | { userId: string };

/**
 * One author's posts, for a profile: fetched for that author alone, so posts older than the feed's
 * first page still show, with the feed's live posts, likes and deletions merged in.
 * Provided per profile page.
 */
@Injectable()
export class AuthorPosts {
  private readonly api = inject(FeedService);
  private readonly feed = inject(FeedStore);

  private readonly author = signal<PostAuthor | null>(null);
  private readonly fetched = signal<PostDto[]>([]);
  readonly loading = signal(false);

  readonly posts = computed(() => {
    const author = this.author();
    if (!author) return [];
    const mine = (post: PostDto) =>
      'botId' in author ? post.author.isBot && post.author.id === author.botId : !post.author.isBot && post.author.id === author.userId;
    // The feed's copy wins: it has the live likes, comments and deletions.
    const live = this.feed.posts().filter(mine);
    const byId = new Map(this.fetched().map((post) => [post.id, post]));
    for (const post of live) byId.set(post.id, post);
    const gone = this.feed.removed();
    return [...byId.values()].filter((post) => !gone.has(post.id)).sort((a, b) => b.createdAt.localeCompare(a.createdAt));
  });

  async load(author: PostAuthor): Promise<void> {
    this.author.set(author);
    this.fetched.set([]);
    this.loading.set(true);
    try {
      const page = await firstValueFrom(
        this.api.apiFeedPostsGet(undefined, PAGE, 'botId' in author ? author.botId : undefined, 'userId' in author ? author.userId : undefined),
      );
      if (this.author() === author) this.fetched.set(page.posts);
    } finally {
      this.loading.set(false);
    }
  }
}
