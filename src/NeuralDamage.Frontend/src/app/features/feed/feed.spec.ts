import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { AttachmentsService } from '../../api/api/attachments.service';
import { FeedService } from '../../api/api/feed.service';
import { AuthService } from '../../core/auth/auth.service';
import { CommentDto, PostDto } from '../../core/models';
import { ChatHubEvents } from '../../core/signalr/hub-events';
import { SignalRService } from '../../core/signalr/signalr.service';
import { BotDirectory } from '../bots/bot-directory';
import { ChatList } from '../chat/chat-list';
import { activityLine, FeedPage } from './feed-page';
import { canDelete, formatAgo, mergePosts, threadOf, withComment, withLike } from './feed-model';
import { textParts } from './feed-text';
import { PostCard } from './post-card';
import { PostThread } from './post-thread';

const author = (id: string, name: string, isBot = true) => ({ id, name, avatarUrl: null, isBot, modelId: isBot ? 'm/x' : null });

function post(id: string, over: Partial<PostDto> = {}): PostDto {
  return {
    id,
    author: author('b1', 'Gordon'),
    content: `post ${id}`,
    sourceChatId: null,
    createdAt: '2026-10-21T10:00:00Z',
    likeCount: 0,
    likedByMe: false,
    commentCount: 0,
    previewComments: [],
    ...over,
  };
}

function comment(id: string, over: Partial<CommentDto> = {}): CommentDto {
  return {
    id,
    postId: 'p1',
    parentId: null,
    author: author('b2', 'Kim'),
    content: `comment ${id}`,
    createdAt: '2026-10-21T10:05:00Z',
    likeCount: 0,
    likedByMe: false,
    ...over,
  };
}

/** A hub that records handlers, so a test can fire events. */
function fakeHub() {
  const handlers = new Map<string, ((...args: unknown[]) => void)[]>();
  return {
    handlers,
    fire<E extends keyof ChatHubEvents>(event: E, ...args: ChatHubEvents[E]) {
      for (const h of handlers.get(event) ?? []) h(...args);
    },
    service: {
      onChatEvent: (e: string, h: (...args: unknown[]) => void) => handlers.set(e, [...(handlers.get(e) ?? []), h]),
      offChatEvent: (e: string, h: (...args: unknown[]) => void) => handlers.set(e, (handlers.get(e) ?? []).filter((x) => x !== h)),
      start: () => Promise.resolve(),
      joinPost: () => Promise.resolve(),
      leavePost: () => Promise.resolve(),
      startFeedTyping: () => undefined,
    },
  };
}

const settle = async (fixture: { whenStable(): Promise<unknown>; detectChanges(): void }) => {
  for (let i = 0; i < 3; i++) {
    await new Promise((r) => setTimeout(r, 0));
    fixture.detectChanges();
    await fixture.whenStable();
  }
};

describe('feed model', () => {
  it('says how long ago, as a feed does', () => {
    const now = new Date('2026-10-21T12:00:00Z');
    expect(formatAgo('2026-10-21T11:59:30Z', now)).toBe('now');
    expect(formatAgo('2026-10-21T11:55:00Z', now)).toBe('5m');
    expect(formatAgo('2026-10-21T10:00:00Z', now)).toBe('2h');
    expect(formatAgo('2026-10-19T12:00:00Z', now)).toBe('2d');
  });

  it('keeps the reader’s own like apart from others’', () => {
    const p = post('p1', { likeCount: 1 });
    expect(withLike(p, { postId: 'p1', commentId: null, likeCount: 2, userId: 'someone', liked: true }, 'me').likedByMe).toBe(false);
    const mine = withLike(p, { postId: 'p1', commentId: null, likeCount: 2, userId: 'me', liked: true }, 'me');
    expect([mine.likeCount, mine.likedByMe]).toEqual([2, true]);
  });

  it('counts a new comment and previews only the last two top-level ones', () => {
    let p = post('p1');
    for (const id of ['c1', 'c2', 'c3']) p = withComment(p, comment(id));
    p = withComment(p, comment('r1', { parentId: 'c1' }));
    expect(p.commentCount).toBe(4);
    expect(p.previewComments.map((c) => c.id)).toEqual(['c2', 'c3']);
    expect(withComment(p, comment('c3')).commentCount).toBe(4);
  });

  it('merges pages newest first without duplicates', () => {
    const merged = mergePosts([post('a', { createdAt: '2026-10-21T10:00:00Z' })], [post('b', { createdAt: '2026-10-21T11:00:00Z' }), post('a')]);
    expect(merged.map((p) => p.id)).toEqual(['b', 'a']);
  });

  it('nests replies one level under their comment', () => {
    const nodes = threadOf([comment('c1'), comment('r1', { parentId: 'c1', createdAt: '2026-10-21T10:06:00Z' }), comment('c2', { createdAt: '2026-10-21T10:07:00Z' })]);
    expect(nodes.map((n) => [n.comment.id, n.replies.map((r) => r.id)])).toEqual([
      ['c1', ['r1']],
      ['c2', []],
    ]);
  });

  it('lets people delete their own, and admins anything', () => {
    expect(canDelete(author('me', 'Me', false), 'me', false)).toBe(true);
    expect(canDelete(author('b1', 'Bot'), 'me', false)).toBe(false);
    expect(canDelete(author('b1', 'Bot'), 'me', true)).toBe(true);
  });

  it('turns @mentions of known names into chips', () => {
    expect(textParts('hey @Kim Jong Un, and @nobody', ['Kim Jong Un'])).toEqual([
      { text: 'hey ', mention: false },
      { text: '@Kim Jong Un', mention: true },
      { text: ', and @nobody', mention: false },
    ]);
    expect(activityLine({ kind: 'comment', actorName: 'Kim', actorIsBot: true, postId: 'p', text: 'Gordon', at: '' })).toBe("commented under Gordon's post");
  });
});

describe('PostCard', () => {
  async function render(p: PostDto, chats: { id: string; name: string }[] = []) {
    await TestBed.configureTestingModule({
      imports: [PostCard],
      providers: [provideRouter([]), { provide: ChatList, useValue: { chats: signal(chats) } }],
    }).compileComponents();
    const fixture = TestBed.createComponent(PostCard);
    fixture.componentRef.setInput('post', p);
    fixture.componentRef.setInput('names', ['Kim']);
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('names the source chat only for its members', async () => {
    const p = post('p1', { sourceChatId: 'chat-1', content: 'ask @Kim' });
    const outsider = await render(p);
    expect(outsider.querySelector('[data-testid=source-chat]')).toBeNull();
    TestBed.resetTestingModule();
    const member = await render(p, [{ id: 'chat-1', name: 'cook' }]);
    expect(member.querySelector('[data-testid=source-chat]')?.textContent).toContain('after the chat in "cook"');
    expect(member.querySelector('[data-testid=mention]')?.textContent).toBe('@Kim');
    expect(member.textContent).toContain('Bot');
  });

  it('shows the last comments under the post', async () => {
    const el = await render(post('p1', { commentCount: 5, previewComments: [comment('c1')] }));
    expect(el.querySelector('[data-testid=preview-comments]')?.textContent).toContain('comment c1');
    expect(el.textContent).toContain('View all 5 comments');
  });
});

function feedApi(posts: PostDto[]) {
  return {
    apiFeedPostsGet: vi.fn(() => of({ posts, canModerate: false })),
    apiFeedActivityGet: () => of({ recent: [{ kind: 'post', actorName: 'Gordon', actorIsBot: true, postId: 'p1', text: 'x', at: new Date().toISOString() }], postsToday: 3, botCommentsToday: 7, storiesLive: 0 }),
    apiFeedUnreadGet: () => of(4),
    apiFeedSeenPost: vi.fn(() => of(null)),
    apiFeedPostsPost: vi.fn((req: { content: string }) => of(post('new', { content: req.content, author: author('me', 'Alice', false), createdAt: new Date().toISOString() }))),
    apiFeedPostsPostIdGet: (id: string) =>
      of({
        post: post(id, { commentCount: 2 }),
        comments: [comment('c1'), comment('r1', { parentId: 'c1', author: author('me', 'Alice', false) })],
        canModerate: false,
      }),
    apiFeedPostsPostIdCommentsPost: vi.fn((postId: string, req: { content: string; parentId: string | null }) =>
      of(comment('c9', { postId, content: req.content, parentId: req.parentId, author: author('me', 'Alice', false) })),
    ),
  };
}

function providers(api: ReturnType<typeof feedApi>, hub: ReturnType<typeof fakeHub>) {
  return [
    provideRouter([]),
    { provide: FeedService, useValue: api },
    { provide: SignalRService, useValue: hub.service },
    { provide: AuthService, useValue: { user: signal({ id: 'me', displayName: 'Alice', email: 'a@example.com' }), displayName: signal('Alice') } },
    { provide: BotDirectory, useValue: { bots: signal([]), load: () => Promise.resolve() } },
    { provide: ChatList, useValue: { chats: signal([]) } },
    { provide: AttachmentsService, useValue: {} },
  ];
}

describe('FeedPage', () => {
  it('lists the posts, the panel and today’s counts, and adds live posts on top', async () => {
    const api = feedApi([post('p1'), post('p2', { createdAt: '2026-10-21T09:00:00Z' })]);
    const hub = fakeHub();
    await TestBed.configureTestingModule({ imports: [FeedPage], providers: providers(api, hub) }).compileComponents();
    const fixture = TestBed.createComponent(FeedPage);
    await settle(fixture);
    const el = fixture.nativeElement as HTMLElement;

    expect([...el.querySelectorAll('[data-testid=post]')].length).toBe(2);
    expect(el.querySelector('[data-testid=posts-today]')?.textContent?.trim()).toBe('3');
    expect(el.textContent).toContain('Happening now');
    expect(api.apiFeedSeenPost).toHaveBeenCalled();

    hub.fire('FeedPostNew', post('p3', { content: 'fresh one', createdAt: '2026-10-21T11:00:00Z' }));
    await settle(fixture);
    expect(el.querySelector('[data-testid=post]')?.textContent).toContain('fresh one');
  });
});

describe('PostThread', () => {
  it('shows replies under their comment, who is typing, and sends a reply to a comment', async () => {
    const api = feedApi([]);
    const hub = fakeHub();
    await TestBed.configureTestingModule({ imports: [PostThread], providers: providers(api, hub) }).compileComponents();
    const fixture = TestBed.createComponent(PostThread);
    fixture.componentRef.setInput('postId', 'p1');
    await settle(fixture);
    const el = fixture.nativeElement as HTMLElement;

    const top = el.querySelector('[data-testid=comment]')!;
    expect(top.querySelector('[data-testid=reply]')?.textContent).toContain('comment r1');

    hub.fire('FeedTyping', 'p1', 'b2', 'Kim', true);
    await settle(fixture);
    expect(el.textContent).toMatch(/Kim\s+is typing/);

    hub.fire('FeedCommentNew', comment('c5', { content: 'live comment', author: author('b2', 'Kim') }));
    await settle(fixture);
    expect(el.textContent).toContain('live comment');
    expect(el.textContent).not.toMatch(/Kim\s+is typing/);

    (top.querySelector('button:nth-of-type(2)') as HTMLButtonElement).click();
    await settle(fixture);
    await (fixture.componentInstance as unknown as { comment(c: string): Promise<void> }).comment('rematch?');
    expect(api.apiFeedPostsPostIdCommentsPost).toHaveBeenCalledWith('p1', { content: 'rematch?', parentId: 'c1', stickerId: null });
  });
});
