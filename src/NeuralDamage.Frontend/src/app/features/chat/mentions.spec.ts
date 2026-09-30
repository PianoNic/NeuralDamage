import { markMentions } from './mentions';

describe('markMentions', () => {
  it('marks known names and leaves the rest', () => {
    expect(markMentions('@Byte that is fair, @nobody', ['Byte'])).toBe(
      '<span class="mention">@Byte</span> that is fair, @nobody',
    );
  });

  it('prefers the longest name and skips code', () => {
    expect(markMentions('hi @Rex Jr and `@Rex`', ['Rex', 'Rex Jr'])).toBe(
      'hi <span class="mention">@Rex Jr</span> and `@Rex`',
    );
  });

  it('ignores email addresses', () => {
    expect(markMentions('mail nic@Rex.ch', ['Rex'])).toBe('mail nic@Rex.ch');
  });
});
