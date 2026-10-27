import { inject, Service, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { StoriesService } from '../../api/api/stories.service';
import { LayoutIssueDto } from '../../api/model/layoutIssueDto';
import { AuthService } from '../../core/auth/auth.service';
import { StoryDto, StoryImageUploadDto, StoryRingDto, StorySpec } from '../../core/models';
import type { PickedSong } from '../music/music-picker';
import { SignalRService } from '../../core/signalr/signalr.service';

/** A new story joins its author's ring, or starts one; the reader's own ring stays first. */
export function withStory(rings: readonly StoryRingDto[], story: StoryDto, me: string | null): StoryRingDto[] {
  const mine = !story.author.isBot && story.author.id === me;
  const i = rings.findIndex((r) => r.author.id === story.author.id && r.author.isBot === story.author.isBot);
  if (i >= 0) {
    if (rings[i].stories.some((s) => s.id === story.id)) return [...rings];
    const ring = { ...rings[i], stories: [...rings[i].stories, story], allSeen: rings[i].allSeen && story.seen };
    return [...rings.slice(0, i), ring, ...rings.slice(i + 1)];
  }
  const ring: StoryRingDto = { author: story.author, stories: [story], allSeen: story.seen || mine, isMine: mine };
  return mine ? [ring, ...rings] : [...rings.filter((r) => r.isMine), ring, ...rings.filter((r) => !r.isMine)];
}

/** A story marked seen; the ring turns grey once all of it is. */
export function withSeen(rings: readonly StoryRingDto[], storyId: string): StoryRingDto[] {
  return rings.map((r) => {
    if (!r.stories.some((s) => s.id === storyId)) return r;
    const stories = r.stories.map((s) => (s.id === storyId ? { ...s, seen: true } : s));
    return { ...r, stories, allSeen: stories.every((s) => s.seen) };
  });
}

export function withoutStory(rings: readonly StoryRingDto[], storyId: string): StoryRingDto[] {
  return rings
    .map((r) => ({ ...r, stories: r.stories.filter((s) => s.id !== storyId) }))
    .filter((r) => r.stories.length > 0);
}

/** The stories row, kept live, and the calls the editor and the viewer make. */
@Service()
export class StoryStore {
  private readonly api = inject(StoriesService);
  private readonly signalr = inject(SignalRService);
  private readonly auth = inject(AuthService);

  readonly rings = signal<StoryRingDto[]>([]);
  private started = false;

  private get me(): string | null {
    return this.auth.user()?.id ?? null;
  }

  async load(): Promise<void> {
    if (!this.started) {
      this.started = true;
      this.signalr.onChatEvent('StoryNew', (story) => this.rings.update((r) => withStory(r, story, this.me)));
      this.signalr.onChatEvent('StoryDeleted', (id) => this.rings.update((r) => withoutStory(r, id)));
      // Stories posted or deleted while offline never arrive live: read the row again.
      this.signalr.reconnected$.subscribe(() => void this.load());
      void this.signalr.start();
    }
    try {
      this.rings.set(await firstValueFrom(this.api.apiStoriesGet()));
    } catch {
      // The row stays as it was.
    }
  }

  /** Drops stories past their 24 hours from what is on screen. */
  pruneExpired(now = new Date()): void {
    const at = now.toISOString();
    this.rings.update((rings) =>
      rings.map((r) => ({ ...r, stories: r.stories.filter((s) => s.expiresAt > at) })).filter((r) => r.stories.length > 0),
    );
  }

  async markSeen(story: StoryDto): Promise<void> {
    if (story.seen) return;
    this.rings.update((r) => withSeen(r, story.id));
    await firstValueFrom(this.api.apiStoriesStoryIdSeenPost(story.id)).catch(() => undefined);
  }

  /** Sends a reply (or a quick reaction) to the author's direct chat; returns the chat. */
  async reply(story: StoryDto, content: string): Promise<string> {
    const result = await firstValueFrom(this.api.apiStoriesStoryIdReplyPost(story.id, { content }));
    return result.chatId;
  }

  async publish(spec: StorySpec, music: PickedSong | null = null): Promise<StoryDto> {
    const story = await firstValueFrom(this.api.apiStoriesPost({ spec, songId: music?.song.id ?? null, musicStart: music?.start ?? null }));
    this.rings.update((r) => withStory(r, story, this.me));
    return story;
  }

  async remove(story: StoryDto): Promise<void> {
    await firstValueFrom(this.api.apiStoriesStoryIdDelete(story.id));
    this.rings.update((r) => withoutStory(r, story.id));
  }

  uploadImage(file: Blob): Promise<StoryImageUploadDto> {
    return firstValueFrom(this.api.apiStoriesImagesPost(file));
  }

  /** What the layout check finds in the spec: overlaps, cut-off layers, small text, low contrast. */
  checkLayout(spec: StorySpec): Promise<LayoutIssueDto[]> {
    return firstValueFrom(this.api.apiStoriesLayoutPost({ spec }));
  }

  /** The PNG the server would post, exactly. */
  preview(spec: StorySpec): Promise<Blob> {
    return firstValueFrom(this.api.apiStoriesPreviewPost({ spec }, 'body', false, { httpHeaderAccept: 'image/png' }));
  }
}
