import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { BotsService } from '../../api/api/bots.service';
import { ChatMember } from '../../core/models';
import { MentionCard, shiftToFit } from './mention-card';

function member(id: string, displayName: string, memberType: 'user' | 'bot'): ChatMember {
  return {
    id,
    chatId: 'chat',
    userId: memberType === 'user' ? id : null,
    botId: memberType === 'bot' ? `bot-${id}` : null,
    role: 'Member',
    joinedAt: new Date().toISOString(),
    displayName,
    avatarUrl: null,
    memberType,
    modelId: null,
    isMuted: false,
    modelStatus: null,
    modelStatusReason: null,
  };
}

describe('MentionCard', () => {
  async function setup() {
    const bots = {
      apiBotsBotIdGet: (id: string) => of({ id, personality: 'Ruthless Decepticon leader\nand more' }),
    };
    await TestBed.configureTestingModule({
      imports: [MentionCard],
      providers: [{ provide: BotsService, useValue: bots }],
    }).compileComponents();
    const fixture = TestBed.createComponent(MentionCard);
    fixture.componentRef.setInput('members', [member('b1', 'Megatron', 'bot'), member('u1', 'Alice', 'user')]);
    fixture.componentRef.setInput('currentUserId', 'me');
    await fixture.whenStable();

    const anchor = document.createElement('span');
    anchor.textContent = '@Megatron';
    document.body.appendChild(anchor);
    const card = () => document.querySelector<HTMLElement>('[data-testid=mention-card]');
    const settle = async () => {
      await fixture.whenStable();
      await new Promise((r) => setTimeout(r, 50));
      await fixture.whenStable();
    };
    return { fixture, component: fixture.componentInstance, anchor, card, settle };
  }

  it('shows who was mentioned, with Mention, Mute and View profile', async () => {
    const { component, anchor, card, settle } = await setup();
    component.show('b1', anchor);
    await settle();

    const text = card()?.textContent ?? '';
    expect(text).toContain('Megatron');
    expect(text).toContain('Bot');
    expect(text).toContain('Ruthless Decepticon leader');
    expect(text).toContain('Mention');
    expect(text).toContain('Mute');
    expect(text).toContain('View profile');
  });

  it('shows when a person joined, and no Mute', async () => {
    const { component, anchor, card, settle } = await setup();
    component.show('u1', anchor);
    await settle();

    const text = card()?.textContent ?? '';
    expect(text).toContain('Alice');
    expect(text).toContain('Joined today');
    expect(text).not.toContain('Mute');
  });

  it('closes on Escape and on a second click on the same mention', async () => {
    const { component, anchor, card, settle } = await setup();
    component.show('b1', anchor);
    await settle();
    expect(card()).not.toBeNull();

    card()!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    await settle();
    expect(component.isOpen()).toBe(false);

    component.show('b1', anchor);
    await settle();
    expect(component.isOpen()).toBe(true);
    component.show('b1', anchor);
    await settle();
    expect(component.isOpen()).toBe(false);
  });

  it('moves the card left on a phone instead of running off the screen', () => {
    expect(shiftToFit(100, 1280)).toBe(0);
    // 390px wide: the card is 320px, so from x=116 it would end at 436.
    expect(shiftToFit(116, 390)).toBe(390 - 16 - 320 - 116);
  });

  it('closes after Mention, handing over the member', async () => {
    const { component, anchor, card, settle } = await setup();
    const mentioned: string[] = [];
    component.mention.subscribe((m) => mentioned.push(m.displayName));
    component.show('b1', anchor);
    await settle();

    [...card()!.querySelectorAll('button')].find((b) => b.textContent?.includes('Mention'))!.click();
    await settle();
    expect(mentioned).toEqual(['Megatron']);
    expect(component.isOpen()).toBe(false);
  });
});
