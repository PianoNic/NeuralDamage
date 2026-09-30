import { TestBed } from '@angular/core/testing';
import { Message } from '../../core/models';
import { withDescription } from './message.mapper';
import { buildTimeline, MessageList, TimelineItem } from './message-list';

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

describe('MessageList drops', () => {
  function dragEvent(type: string, files: File[]): DragEvent {
    const event = new Event(type, { bubbles: true, cancelable: true }) as DragEvent;
    Object.defineProperty(event, 'dataTransfer', { value: { types: ['Files'], files, dropEffect: 'none' } });
    return event;
  }

  it('takes images dropped anywhere on the list, with an overlay while dragging', async () => {
    const fixture = TestBed.createComponent(MessageList);
    // Empty, so the chat container (which needs a real layout engine) stays out of it.
    fixture.componentRef.setInput('messages', []);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    const dropped: File[][] = [];
    fixture.componentInstance.filesDropped.subscribe((files) => dropped.push(files));

    const over = dragEvent('dragover', []);
    host.querySelector('hlm-empty')!.dispatchEvent(over);
    await fixture.whenStable();
    expect(over.defaultPrevented).toBe(true);
    expect(host.textContent).toContain('Drop images to attach them');

    const file = new File(['x'], 'cat.png', { type: 'image/png' });
    const drop = dragEvent('drop', [file]);
    host.dispatchEvent(drop);
    await fixture.whenStable();
    expect(drop.defaultPrevented).toBe(true);
    expect(dropped).toEqual([[file]]);
    expect(host.textContent).not.toContain('Drop images to attach them');
  });
});

describe('withDescription', () => {
  const withImage = (): Message => ({
    ...message('a', 0),
    attachments: [
      { id: 'img', url: '/x', contentType: 'image/png', sizeBytes: 1, description: null },
    ],
  });

  it('fills in the described image', () => {
    const list = [withImage(), message('b', 1)];
    const next = withDescription(list, 'a', 'img', 'A cat.');
    expect(next[0].attachments[0].description).toBe('A cat.');
    expect(next[1]).toBe(list[1]);
  });

  it('leaves the list alone for an image it does not have', () => {
    const list = [withImage()];
    expect(withDescription(list, 'a', 'other', 'x')).toBe(list);
    expect(withDescription(list, 'zzz', 'img', 'x')).toBe(list);
  });
});
