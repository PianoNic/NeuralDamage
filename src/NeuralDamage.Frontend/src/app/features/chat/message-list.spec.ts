import { Message } from '../../core/models';
import { buildTimeline, TimelineItem } from './message-list';

function message(id: string, minute: number, replyTo?: string): Message {
  return {
    id,
    chatId: 'chat',
    senderUserId: null,
    senderBotId: `bot-${id}`,
    senderName: `Bot ${id}`,
    senderAvatar: null,
    senderModelId: null,
    senderType: 'bot',
    content: id,
    mentions: [],
    reactions: [],
    attachments: [],
    replyTo: replyTo ? { id: replyTo, senderName: 'x', senderType: 'bot', content: 'x' } : null,
    createdAt: new Date(2026, 8, 30, 12, minute).toISOString(),
  };
}

function shown(items: TimelineItem[]): Record<string, boolean> {
  return Object.fromEntries(
    items.flatMap((item) => (item.kind === 'message' ? [[item.message.id, item.showReply]] : [])),
  );
}

describe('buildTimeline reply references', () => {
  const now = new Date(2026, 8, 30, 13);

  it('hides the reference when the reply target is the message right above', () => {
    const items = buildTimeline([message('a', 0), message('b', 1, 'a')], [], now);
    expect(shown(items)).toEqual({ a: false, b: false });
  });

  it('shows it when something came in between', () => {
    const items = buildTimeline([message('a', 0), message('b', 1), message('c', 2, 'a')], [], now);
    expect(shown(items)['c']).toBe(true);
  });

  it('shows it when the target is not loaded', () => {
    const items = buildTimeline([message('b', 1, 'gone')], [], now);
    expect(shown(items)['b']).toBe(true);
  });

  it('ignores system notices between the two messages', () => {
    const items = buildTimeline(
      [message('a', 0), message('b', 2, 'a')],
      [{ chatId: 'chat', content: 'Rex was muted.', timestamp: new Date(2026, 8, 30, 12, 1).toISOString() }],
      now,
    );
    expect(shown(items)['b']).toBe(false);
  });
});
