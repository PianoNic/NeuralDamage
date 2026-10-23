import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { of, Subject } from 'rxjs';
import { ChatsService } from '../../api/api/chats.service';
import { MessagesService } from '../../api/api/messages.service';
import { AuthService } from '../../core/auth/auth.service';
import { ChatDto, MessageDto } from '../../core/models';
import { Reconnected, SignalRService } from '../../core/signalr/signalr.service';
import { ChatList } from './chat-list';

const chat = (id: string, updatedAt = '2026-10-21T11:00:00Z'): ChatDto => ({
  id,
  name: id,
  createdById: 'me',
  createdAt: '2026-10-21T10:00:00Z',
  updatedAt,
});

const message = (id: string, chatId: string, createdAt: string, senderUserId = 'bob'): MessageDto => ({
  id,
  chatId,
  senderUserId,
  content: id,
  createdAt,
  reactions: [],
  attachments: [],
});

describe('ChatList after a reconnect', () => {
  const reconnected = new Subject<Reconnected>();
  const handlers = new Map<string, (...args: unknown[]) => void>();
  let chats: ChatDto[];
  let missed: Record<string, MessageDto[]>;

  beforeEach(() => {
    chats = [chat('a'), chat('b')];
    missed = {};
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: { user: signal({ id: 'me' }), isAdmin: signal(false) } },
        { provide: ChatsService, useValue: { apiChatsGet: () => of(chats) } },
        {
          provide: MessagesService,
          useValue: { apiChatsChatIdMessagesGet: (chatId: string) => of(missed[chatId] ?? []) },
        },
        {
          provide: SignalRService,
          useValue: {
            reconnected$: reconnected,
            start: async () => undefined,
            onChatEvent: (event: string, cb: (...args: unknown[]) => void) => handlers.set(event, cb),
            onUserEvent: (event: string, cb: (...args: unknown[]) => void) => handlers.set(event, cb),
          },
        },
      ],
    });
  });

  it('picks up chats joined while away, and counts what was missed once', async () => {
    const list = TestBed.inject(ChatList);
    await list.start();

    // Arrived live before the drop: counted already, and must not be counted again.
    const live = message('m1', 'b', '2026-10-21T12:00:00Z');
    handlers.get('MessageNew')!(live);

    chats = [...chats, chat('c')];
    missed = {
      b: [live, message('m2', 'b', '2026-10-21T12:01:00Z')],
      c: [message('m3', 'c', '2026-10-21T12:02:00Z'), message('m4', 'c', '2026-10-21T12:03:00Z', 'me')],
    };
    reconnected.next({ since: Date.parse('2026-10-21T12:00:30Z') });
    await new Promise((resolve) => setTimeout(resolve));

    expect(list.chats().map((c) => c.id)).toEqual(['c', 'b', 'a']);
    expect(list.unread().get('b')).toBe(2);
    // The user's own message is not unread.
    expect(list.unread().get('c')).toBe(1);
  });
});
