import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { MemberAvatar } from '../../shared/member-avatar';
import { TypingRow, typingParts } from './typing-row';

const text = (names: string[]) =>
  typingParts(names)
    .map((part) => part.text)
    .join('');

describe('typingParts', () => {
  it('reads naturally for one, two and three people', () => {
    expect(text(['Rex'])).toBe('Rex is typing');
    expect(text(['Rex', 'alice'])).toBe('Rex and alice are typing');
    expect(text(['Rex', 'Juno', 'alice'])).toBe('Rex, Juno and alice are typing');
  });

  it('counts the rest once more than three are typing', () => {
    expect(text(['Rex', 'Juno', 'alice', 'Byte', 'bob'])).toBe('Rex, Juno and 3 others are typing');
  });
});

describe('TypingRow', () => {
  it("renders every typer with the shared member avatar, so bots get their vendor icon", async () => {
    const fixture = TestBed.createComponent(TypingRow);
    fixture.componentRef.setInput('typers', [
      { id: 'b1', name: 'Rex', avatarUrl: null, modelId: 'openai/gpt-6-luna' },
      { id: 'u1', name: 'alice', avatarUrl: null },
    ]);
    fixture.detectChanges();
    await fixture.whenStable();

    // The same avatar as the message list, header and people panel: vendor icon, dark:invert included.
    const avatars = fixture.debugElement.queryAll(By.directive(MemberAvatar)).map((d) => d.componentInstance as MemberAvatar);
    expect(avatars.map((a) => [a.name(), a.modelId() ?? null])).toEqual([
      ['Rex', 'openai/gpt-6-luna'],
      ['alice', null],
    ]);
  });
});
