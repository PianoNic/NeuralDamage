import { TestBed } from '@angular/core/testing';
import { Message } from '../../core/models';
import { MentionTarget } from './mentions';
import { MessageItem } from './message-item';

function message(content: string): Message {
  return {
    id: 'm1',
    chatId: 'chat',
    senderUserId: 'u1',
    senderBotId: null,
    senderName: 'Alice',
    senderAvatar: null,
    senderModelId: null,
    senderType: 'user',
    content,
    mentions: [],
    reactions: [],
    attachments: [],
    sticker: null,
    story: null,
    replyTo: null,
    createdAt: new Date(2026, 8, 30, 12).toISOString(),
  };
}

const targets: MentionTarget[] = [
  { name: 'Megatron', memberId: 'b1' },
  { name: 'megs', memberId: 'b1' },
  { name: 'Maggus Rühl', memberId: 'u2' },
];

describe('MessageItem mentions', () => {
  async function render(content: string) {
    await TestBed.configureTestingModule({ imports: [MessageItem] }).compileComponents();
    const fixture = TestBed.createComponent(MessageItem);
    fixture.componentRef.setInput('message', message(content));
    fixture.componentRef.setInput('mentionTargets', targets);
    await fixture.whenStable();
    const opened: { memberId: string; anchor: HTMLElement }[] = [];
    fixture.componentInstance.openMention.subscribe((e) => opened.push(e));
    return { el: fixture.nativeElement as HTMLElement, opened };
  }

  it('highlights members, nicknames included, and nobody else', async () => {
    const { el } = await render('@Maggus Rühl ask @megs, not @Starscream');
    const mentions = [...el.querySelectorAll<HTMLElement>('.mention')];
    expect(mentions.map((m) => [m.textContent, m.dataset['memberId']])).toEqual([
      ['@Maggus Rühl', 'u2'],
      ['@megs', 'b1'],
    ]);
    expect(el.textContent).toContain('not @Starscream');
  });

  it('opens the card from a click or Enter on a mention, anchored to it', async () => {
    const { el, opened } = await render('hi @Megatron');
    const mention = el.querySelector<HTMLElement>('.mention')!;
    expect(mention.getAttribute('role')).toBe('button');
    expect(mention.getAttribute('tabindex')).toBe('0');

    mention.click();
    mention.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    expect(opened).toEqual([
      { memberId: 'b1', anchor: mention },
      { memberId: 'b1', anchor: mention },
    ]);

    el.querySelector<HTMLElement>('pk-markdown')!.click();
    expect(opened.length).toBe(2);
  });
});

describe('MessageItem story replies', () => {
  async function label(msg: Message, currentUserId: string) {
    await TestBed.configureTestingModule({ imports: [MessageItem] }).compileComponents();
    const fixture = TestBed.createComponent(MessageItem);
    fixture.componentRef.setInput('message', msg);
    fixture.componentRef.setInput('currentUserId', currentUserId);
    await fixture.whenStable();
    return (fixture.nativeElement as HTMLElement).querySelector('[data-testid="story-label"]')?.textContent?.trim() ?? null;
  }

  const storyReply = (authorUserId: string | null): Message => ({
    ...message('💀'),
    story: { storyId: 's1', authorName: 'Kim', authorUserId, authorBotId: null, text: 'Beach day' },
  });

  it('says whose story it replied to', async () => {
    expect(await label(storyReply('kim'), 'u1')).toBe("Replied to Kim's story");
  });

  it('says "your story" to the one who posted it', async () => {
    expect(await label(storyReply('me'), 'me')).toBe('Replied to your story');
  });

  it('shows nothing on an ordinary message', async () => {
    expect(await label(message('hi'), 'u1')).toBeNull();
  });
});
