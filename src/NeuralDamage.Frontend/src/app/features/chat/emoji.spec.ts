import { TestBed } from '@angular/core/testing';
import { convertShortcodes, EmojiIndex, loadEmojis, recentEmojis, rememberEmoji, searchEmojis, trailingShortcode } from './emoji';
import { EmojiPicker } from './emoji-picker';
import { findMentions } from './mentions';

let index: EmojiIndex;
beforeAll(async () => {
  index = await loadEmojis();
});

describe('emoji shortcodes', () => {
  it('has the familiar GitHub/Discord names', () => {
    for (const [code, emoji] of [['sob', '😭'], ['joy', '😂'], ['skull', '💀'], ['fire', '🔥'], ['pray', '🙏'], ['clown_face', '🤡'], ['+1', '👍']]) {
      expect(index.byShortcode.get(code)?.emoji.replace(/️/g, '')).toBe(emoji);
    }
  });

  it('triggers on a colon at a word start with at least two characters', () => {
    expect(trailingShortcode(':so')).toEqual({ start: 0, query: 'so' });
    expect(trailingShortcode('hey :SOB')).toEqual({ start: 4, query: 'sob' });
    expect(trailingShortcode('hey\n:jo')).toEqual({ start: 4, query: 'jo' });
    expect(trailingShortcode('hey :s')).toBeNull();
    expect(trailingShortcode('hey:so')).toBeNull();
    expect(trailingShortcode('at 10:30')).toBeNull();
    expect(trailingShortcode('see https://example.com')).toBeNull();
    expect(trailingShortcode('see http:sob')).toBeNull();
    expect(trailingShortcode('hey :sob:')).toBeNull();
  });

  it('never triggers inside a mention chip', () => {
    const text = 'hi @Dr :sob';
    expect(trailingShortcode(text, findMentions(text, ['Dr :sob']))).toBeNull();
    expect(trailingShortcode(text, findMentions(text, ['Dr']))).toEqual({ start: 7, query: 'sob' });
  });

  it('ranks the exact shortcode, then prefixes, then keywords', () => {
    const sob = searchEmojis(index, 'sob');
    expect(sob[0].emoji).toBe('😭');
    expect(searchEmojis(index, 'so', 8).every((e) => e.shortcodes.some((c) => c.startsWith('so')))).toBe(true);

    const laugh = searchEmojis(index, 'laugh').map((e) => e.emoji);
    expect(laugh[0]).toBe('😆'); // "laughing" is a shortcode prefix ...
    expect(laugh).toContain('😂'); // ... 😂 comes by its keyword
    expect(laugh.indexOf('😂')).toBeGreaterThan(laugh.indexOf('😆'));

    // "face" starts a word of "clown_face": ranked above emoji that only have it as a keyword.
    const face = searchEmojis(index, 'face');
    const clown = face.findIndex((e) => e.emoji === '🤡');
    const keywordOnly = face.findIndex((e) => !e.shortcodes.some((c) => c.split(/[_-]/).some((w) => w.startsWith('face'))));
    expect(clown).toBeLessThan(keywordOnly);
    expect(searchEmojis(index, 'so', 8)).toHaveLength(8);
  });

  it('converts complete known codes only, and only where the text changed', () => {
    expect(convertShortcodes('lol :joy:', index)).toEqual({ start: 4, end: 9, text: '😂' });
    expect(convertShortcodes(':joy::sob: x :nope: :fire:', index)).toEqual({ start: 0, end: 26, text: '😂😭 x :nope: 🔥' });
    expect(convertShortcodes('lol :nope:', index)).toBeNull();
    expect(convertShortcodes('10:30:45', index)).toBeNull();
    expect(convertShortcodes('a:joy:', index)).toBeNull();
    // Typed text before the change is left as it is.
    expect(convertShortcodes(':joy: and :sob:', index, 10)).toEqual({ start: 10, end: 15, text: '😭' });
    // A chip whose name holds a code stays a chip.
    const text = '@Mr :joy: hi';
    expect(convertShortcodes(text, index, 0, text.length, findMentions(text, ['Mr :joy:']))).toBeNull();
  });
});

describe('recent emoji', () => {
  beforeEach(() => localStorage.clear());

  it('keeps the last used first, without duplicates, up to a row', () => {
    for (const e of ['😂', '🔥', '😂', '💀', '1', '2', '3', '4', '5', '6']) rememberEmoji(e);
    expect(recentEmojis()).toEqual(['6', '5', '4', '3', '2', '1', '💀', '😂']);
  });

  it('survives storage that is broken or unavailable', () => {
    localStorage.setItem('nd.emoji.recent', '{not json');
    expect(recentEmojis()).toEqual([]);
    const spy = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('QuotaExceeded');
    });
    expect(rememberEmoji('😂')).toEqual(['😂']);
    spy.mockRestore();
  });
});

describe('EmojiPicker', () => {
  beforeEach(() => localStorage.clear());

  async function setup() {
    await TestBed.configureTestingModule({ imports: [EmojiPicker] }).compileComponents();
    const fixture = TestBed.createComponent(EmojiPicker);
    await fixture.whenStable();
    await loadEmojis();
    await fixture.whenStable();
    const picked: string[] = [];
    fixture.componentInstance.pick.subscribe((e) => picked.push(e));
    return { fixture, picker: fixture.componentInstance, picked };
  }

  const emojisOf = (rows: ReturnType<EmojiPicker['rows']>) => rows.flatMap((r) => (r.kind === 'emojis' ? r.emojis.map((e) => e.emoji) : []));

  it('lists every category, eight to a row', async () => {
    const { picker } = await setup();
    const headers = picker.rows().filter((r) => r.kind === 'header').map((r) => (r.kind === 'header' ? r.label : ''));
    expect(headers).toEqual([
      'Smileys & emotion', 'People & body', 'Animals & nature', 'Food & drink', 'Travel & places',
      'Activities', 'Objects', 'Symbols', 'Flags',
    ]);
    expect(picker.rows().every((r) => r.kind === 'header' || r.emojis.length <= 8)).toBe(true);
  });

  it('renders only the rows in view, and the flags once scrolled to them', async () => {
    const { fixture } = await setup();
    const host = fixture.nativeElement as HTMLElement;
    const buttons = () => [...host.querySelectorAll('[data-testid=emoji-grid] button')];
    expect(buttons().length).toBeLessThanOrEqual(20 * 8);
    expect(buttons()[0].textContent?.trim()).toBe('😀');

    (host.querySelector('[aria-label=Flags]') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(host.textContent).toContain('🇨🇭');
    expect(buttons().some((b) => b.textContent?.trim() === '😀')).toBe(false);
  });

  it('searches by shortcode and keyword', async () => {
    const { fixture, picker } = await setup();
    picker.setQuery('skull');
    await fixture.whenStable();
    expect(emojisOf(picker.rows()).slice(0, 2)).toEqual(['💀', '☠️']);
    picker.setQuery('zzzzqq');
    await fixture.whenStable();
    expect(picker.rows()).toEqual([]);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No emoji found');
  });

  it('picks with Enter on the search field, and puts the pick in the recent row', async () => {
    const { fixture, picker, picked } = await setup();
    const input = (fixture.nativeElement as HTMLElement).querySelector('input')!;
    input.value = 'fire';
    input.dispatchEvent(new Event('input'));
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    await fixture.whenStable();
    expect(picked).toEqual(['🔥']);
    expect(recentEmojis()).toEqual(['🔥']);

    picker.setQuery('');
    await fixture.whenStable();
    const [header, row] = picker.rows();
    expect(header.kind === 'header' && header.label).toBe('Recent');
    expect(emojisOf([row])).toEqual(['🔥']);
  });

  it('starts with the recent row kept in this browser', async () => {
    rememberEmoji('💀');
    rememberEmoji('😭');
    const { picker } = await setup();
    expect(emojisOf(picker.rows().slice(0, 2))).toEqual(['😭', '💀']);
  });
});
