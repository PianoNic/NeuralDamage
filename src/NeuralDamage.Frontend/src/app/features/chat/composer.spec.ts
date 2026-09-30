import { TestBed } from '@angular/core/testing';
import { ChatMember } from '../../core/models';
import { Composer, MAX_MESSAGE_LENGTH } from './composer';

function member(id: string, displayName: string, memberType: 'user' | 'bot'): ChatMember {
  return {
    id,
    chatId: 'chat',
    userId: memberType === 'user' ? id : null,
    botId: memberType === 'bot' ? id : null,
    role: 'member',
    joinedAt: '',
    displayName,
    avatarUrl: null,
    memberType,
    modelId: null,
    isMuted: false,
    modelStatus: null,
    modelStatusReason: null,
  };
}

describe('Composer', () => {
  async function setup() {
    await TestBed.configureTestingModule({ imports: [Composer] }).compileComponents();
    const fixture = TestBed.createComponent(Composer);
    fixture.componentRef.setInput('members', [
      member('u1', 'Alice', 'user'),
      member('me', 'Nic', 'user'),
      member('b1', 'Gemini', 'bot'),
      member('b2', 'Grok', 'bot'),
    ]);
    fixture.componentRef.setInput('currentUserId', 'me');
    await fixture.whenStable();
    const textarea = (fixture.nativeElement as HTMLElement).querySelector('textarea')!;
    const type = async (value: string) => {
      textarea.value = value;
      textarea.dispatchEvent(new Event('input'));
      await fixture.whenStable();
    };
    const press = async (key: string) => {
      const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
      textarea.dispatchEvent(event);
      await fixture.whenStable();
      return event;
    };
    return { fixture, component: fixture.componentInstance, type, press };
  }

  it('suggests slash commands by prefix', async () => {
    const { component, type } = await setup();
    await type('/mu');
    expect(component.suggestions().map((s) => s.key)).toEqual(['/mute']);
  });

  it('completes bot names after a command that takes one', async () => {
    const { component, type } = await setup();
    await type('/kick g');
    expect(component.suggestions().map((s) => s.result)).toEqual(['/kick Gemini', '/kick Grok']);
  });

  it('never offers yourself as a mention', async () => {
    const { component, type } = await setup();
    await type('@');
    expect(component.suggestions().map((s) => s.label)).toEqual(['Alice', 'Gemini', 'Grok']);
  });

  it('navigates suggestions with the keyboard and picks with Tab', async () => {
    const { component, type, press } = await setup();
    await type('@');
    await press('ArrowDown');
    expect(component.activeIndex()).toBe(1);
    await press('Tab');
    expect(component.content()).toBe('@Gemini ');
  });

  it('picks with Enter instead of sending while the popup is open', async () => {
    const { component, type, press } = await setup();
    const sent: string[] = [];
    component.send.subscribe((e) => sent.push(e.content));
    await type('/st');
    await press('Enter');
    expect(component.content()).toBe('/stop');
    expect(sent).toEqual([]);
  });

  it('sends on Enter once there is nothing to pick', async () => {
    const { component, type, press } = await setup();
    const sent: string[] = [];
    component.send.subscribe((e) => sent.push(e.content));
    await type('hello there');
    await press('Enter');
    expect(sent).toEqual(['hello there']);
    expect(component.content()).toBe('');
  });

  it('refuses to send a message over the limit and says so', async () => {
    const { fixture, component, type, press } = await setup();
    const sent: string[] = [];
    component.send.subscribe((e) => sent.push(e.content));
    await type('x'.repeat(MAX_MESSAGE_LENGTH + 1));
    await press('Enter');
    expect(sent).toEqual([]);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain(`${MAX_MESSAGE_LENGTH + 1} / ${MAX_MESSAGE_LENGTH}`);
  });

  it('closes the popup on Esc, then Esc cancels a reply', async () => {
    const { fixture, component, type, press } = await setup();
    let cancelled = 0;
    component.cancelReply.subscribe(() => cancelled++);
    fixture.componentRef.setInput('replyingTo', { id: 'm1', senderName: 'Alice', content: 'hi' });
    await type('@');
    await press('Escape');
    expect(component.suggestions()).toEqual([]);
    expect(cancelled).toBe(0);
    await press('Escape');
    expect(cancelled).toBe(1);
  });

  it('announces typing only for non-empty input', async () => {
    const { component, type } = await setup();
    let typing = 0;
    component.typing.subscribe(() => typing++);
    await type('h');
    await type('');
    expect(typing).toBe(1);
  });
});
