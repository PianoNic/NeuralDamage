import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { of, Subject } from 'rxjs';
import { ChatsService } from '../../api/api/chats.service';
import { MessagesService } from '../../api/api/messages.service';
import { ReactionsService } from '../../api/api/reactions.service';
import { AuthService } from '../../core/auth/auth.service';
import { ChatDetailDto, MessageDto, ReactionGroupDto } from '../../core/models';
import { Reconnected, SignalRService } from '../../core/signalr/signalr.service';
import { BotDetails } from '../bots/bot-details';
import { StickerLibrary } from '../stickers/sticker-library';
import { Chat } from './chat';
import { ChatList } from './chat-list';
import { mergeMessages, toMessages } from './message.mapper';

const CHAT_ID = 'chat-1';

function dto(n: number, extra: Partial<MessageDto> = {}): MessageDto {
  return {
    id: `m${n}`,
    chatId: CHAT_ID,
    senderUserId: 'bob',
    content: `message ${n}`,
    // Unpadded fractions, as the API sends them: `.5` is later than `.25`.
    createdAt: `2026-10-21T12:00:${String(n).padStart(2, '0')}.${n % 2 ? '5' : '25'}Z`,
    reactions: [],
    attachments: [],
    ...extra,
  };
}

describe('mergeMessages', () => {
  it('adds what is new, keeps one copy of each message and sorts by time', () => {
    const shown = toMessages([dto(1), dto(2), dto(3)]);
    const fetched = toMessages([dto(5), dto(3), dto(4)]);

    expect(mergeMessages(shown, fetched).map((m) => m.id)).toEqual(['m1', 'm2', 'm3', 'm4', 'm5']);
  });

  it('takes the fetched copy, whose reactions are current', () => {
    const reactions: ReactionGroupDto[] = [{ emoji: '👍', count: 1, reactors: [] } as unknown as ReactionGroupDto];
    const merged = mergeMessages(toMessages([dto(1)]), toMessages([dto(1, { reactions })]));

    expect(merged).toHaveLength(1);
    expect(merged[0].reactions).toEqual(reactions);
  });

  it('compares times as dates, not strings', () => {
    // As text "00.5Z" sorts before "00Z", though it is half a second later.
    const early = dto(1, { id: 'a', createdAt: '2026-10-21T12:00:00Z' });
    const late = dto(2, { id: 'b', createdAt: '2026-10-21T12:00:00.5Z' });
    expect(mergeMessages([], toMessages([late, early])).map((m) => m.id)).toEqual(['a', 'b']);
  });
});

describe('Chat after a reconnect', () => {
  const reconnected = new Subject<Reconnected>();
  const handlers = new Map<string, (...args: unknown[]) => void>();
  let server: MessageDto[];
  let messagesGet: ReturnType<typeof vi.fn>;

  const chatDetail: ChatDetailDto = {
    id: CHAT_ID,
    name: 'General',
    createdById: 'me',
    createdAt: '2026-10-21T11:00:00Z',
    updatedAt: '2026-10-21T11:00:00Z',
    members: [],
  };

  beforeEach(() => {
    handlers.clear();
    server = [dto(1), dto(2), dto(3)];
    // The API's paging: newest page by default, oldest page after a cursor.
    messagesGet = vi.fn((_chatId: string, limit = 50, _before?: string, after?: string) => {
      const sorted = [...server].sort((a, b) => Date.parse(a.createdAt) - Date.parse(b.createdAt));
      if (after) return of(sorted.filter((m) => Date.parse(m.createdAt) > Date.parse(after)).slice(0, limit));
      return of(sorted.slice(-limit));
    });

    TestBed.configureTestingModule({
      providers: [
        { provide: Router, useValue: { navigate: vi.fn() } },
        { provide: AuthService, useValue: { user: signal({ id: 'me' }) } },
        {
          provide: ChatList,
          useValue: { activeId: signal(null), markRead: vi.fn(), wasDeletedHere: () => false },
        },
        { provide: ChatsService, useValue: { apiChatsChatIdGet: vi.fn(() => of(chatDetail)) } },
        { provide: MessagesService, useValue: { apiChatsChatIdMessagesGet: messagesGet } },
        { provide: ReactionsService, useValue: {} },
        { provide: StickerLibrary, useValue: {} },
        { provide: BotDetails, useValue: { byId: signal(new Map()), loadMissing: vi.fn() } },
        {
          provide: SignalRService,
          useValue: {
            reconnected$: reconnected,
            joinChat: vi.fn(async () => undefined),
            leaveChat: vi.fn(async () => undefined),
            onChatEvent: (event: string, cb: (...args: unknown[]) => void) => handlers.set(event, cb),
            offChatEvent: vi.fn(),
            onUserEvent: vi.fn(),
            offUserEvent: vi.fn(),
          },
        },
      ],
    });
    TestBed.overrideComponent(Chat, { set: { template: '', imports: [] } });
  });

  const settle = () => new Promise((resolve) => setTimeout(resolve));

  async function openChat() {
    const fixture = TestBed.createComponent(Chat);
    fixture.componentRef.setInput('chatId', CHAT_ID);
    fixture.detectChanges();
    await fixture.whenStable();
    await settle();
    // Protected state, read the way the template does.
    return fixture.componentInstance as unknown as {
      messages: () => { id: string }[];
      typers: () => { id: string }[];
    };
  }

  it('shows what was sent while away, once each and in order', async () => {
    const chat = await openChat();
    // One message made it over the socket just before it dropped.
    server.push(dto(4));
    handlers.get('MessageNew')!(dto(4));

    server.push(dto(5), dto(6));
    reconnected.next({ since: Date.now() });
    await settle();

    expect(chat.messages().map((m) => m.id)).toEqual(['m1', 'm2', 'm3', 'm4', 'm5', 'm6']);
  });

  it('reads forward from the newest message shown when more than a page was missed', async () => {
    const chat = await openChat();
    for (let n = 4; n <= 59; n++) server.push(dto(n));

    reconnected.next({ since: Date.now() });
    await settle();

    const ids = chat.messages().map((m) => m.id);
    expect(ids).toHaveLength(59);
    expect(new Set(ids).size).toBe(59);
    expect(ids[0]).toBe('m1');
    expect(ids.at(-1)).toBe('m59');
    expect(messagesGet).toHaveBeenCalledWith(CHAT_ID, 100, undefined, dto(3).createdAt);
  });

  it('drops typing indicators left over from before the outage', async () => {
    const chat = await openChat();
    handlers.get('UserTyping')!(CHAT_ID, 'bob', 'Bob');
    handlers.get('BotTyping')!(CHAT_ID, 'bot-1', 'Rex');
    expect(chat.typers()).toHaveLength(2);

    reconnected.next({ since: Date.now() });
    await settle();

    expect(chat.typers()).toEqual([]);
  });
});
