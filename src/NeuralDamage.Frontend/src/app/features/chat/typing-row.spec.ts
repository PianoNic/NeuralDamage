import { typingParts } from './typing-row';

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
