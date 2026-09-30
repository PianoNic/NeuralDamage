import { TestBed } from '@angular/core/testing';
import { ChatMember } from '@app/models';
import { MessageInputComponent } from './message-input';

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
  };
}

describe('MessageInputComponent', () => {
  async function setup() {
    await TestBed.configureTestingModule({ imports: [MessageInputComponent] }).compileComponents();
    const fixture = TestBed.createComponent(MessageInputComponent);
    fixture.componentRef.setInput('members', [
      member('u1', 'Alice', 'user'),
      member('b1', 'Gemini', 'bot'),
      member('b2', 'Grok', 'bot'),
    ]);
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
