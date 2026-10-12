import { findMentions, markMentions, nicknames } from './mentions';

const chip = (id: string, name: string) =>
  `<span class="mention" data-member-id="${id}" role="button" tabindex="0" aria-haspopup="dialog">@${name}</span>`;

describe('markMentions', () => {
  it('marks members and leaves everyone else as plain text', () => {
    expect(markMentions('@Byte that is fair, @nobody', [{ name: 'Byte', memberId: 'b1' }])).toBe(
      `${chip('b1', 'Byte')} that is fair, @nobody`,
    );
  });

  it('marks a nickname as the member it belongs to', () => {
    const targets = [
      { name: 'Megatron', memberId: 'b1' },
      { name: 'megs', memberId: 'b1' },
    ];
    expect(markMentions('@megs and @MEGATRON', targets)).toBe(`${chip('b1', 'megs')} and ${chip('b1', 'MEGATRON')}`);
  });

  it('prefers the longest name and skips code', () => {
    const targets = [
      { name: 'Rex', memberId: 'r' },
      { name: 'Rex Jr', memberId: 'rj' },
    ];
    expect(markMentions('hi @Rex Jr and `@Rex`', targets)).toBe(`hi ${chip('rj', 'Rex Jr')} and \`@Rex\``);
  });

  it('handles names with spaces and umlauts', () => {
    expect(markMentions('@Maggus Rühl!', [{ name: 'Maggus Rühl', memberId: 'm' }])).toBe(`${chip('m', 'Maggus Rühl')}!`);
    // "@Maggus Rühlmann" is someone else.
    expect(markMentions('@Maggus Rühlmann', [{ name: 'Maggus Rühl', memberId: 'm' }])).toBe('@Maggus Rühlmann');
  });

  it('ignores email addresses', () => {
    expect(markMentions('mail nic@Rex.ch or jürg@Rex', [{ name: 'Rex', memberId: 'r' }])).toBe('mail nic@Rex.ch or jürg@Rex');
  });

  it('escapes what it puts into the HTML', () => {
    expect(markMentions('@a<b', [{ name: 'a<b', memberId: '"x' }])).toBe(chip('&#34;x', 'a&#60;b'));
  });
});

describe('findMentions', () => {
  it('finds each mention where the server would', () => {
    expect(findMentions('@Alice @Gemini, hi @Alicex', ['Alice', 'Gemini'])).toEqual([
      { start: 0, end: 6, name: 'Alice' },
      { start: 7, end: 14, name: 'Gemini' },
    ]);
  });
});

describe('nicknames', () => {
  it('splits and trims', () => {
    expect(nicknames(' megs, , big m ')).toEqual(['megs', 'big m']);
    expect(nicknames(null)).toEqual([]);
  });
});
