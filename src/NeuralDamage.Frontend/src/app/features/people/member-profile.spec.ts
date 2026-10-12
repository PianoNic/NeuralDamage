import { TestBed } from '@angular/core/testing';
import { BotDto, ChatMember } from '../../core/models';
import { ModelCatalog } from '../bots/model-catalog';
import { MemberProfile } from './member-profile';

const member = { memberType: 'bot', displayName: 'Megatron', modelId: 'm' } as unknown as ChatMember;
const bot = (systemPrompt: string) =>
  ({ systemPrompt, personality: 'Ruthless Decepticon leader', aliases: 'megs', temperature: 1, repliesToday: 0 }) as unknown as BotDto;

function render(systemPrompt: string) {
  TestBed.configureTestingModule({ providers: [{ provide: ModelCatalog, useValue: { nameOf: () => 'Model' } }] });
  const fixture = TestBed.createComponent(MemberProfile);
  fixture.componentRef.setInput('member', member);
  fixture.componentRef.setInput('bot', bot(systemPrompt));
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

const readMore = (el: HTMLElement) => [...el.querySelectorAll('button')].find((b) => b.textContent?.trim() === 'Read more');

describe('MemberProfile', () => {
  it('cuts a long character and offers to read the rest', () => {
    const el = render('You are Megatron. '.repeat(40));
    expect(el.querySelector('.line-clamp-4')).not.toBeNull();
    expect(readMore(el)).toBeTruthy();
    expect(el.textContent).toContain('Nicknames: megs.');
  });

  it('shows a short character in full, without the button', () => {
    const el = render('You are Megatron.');
    expect(el.textContent).toContain('You are Megatron.');
    expect(readMore(el)).toBeUndefined();
  });
});
