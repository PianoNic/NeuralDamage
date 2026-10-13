import { MentionRange } from './mentions';

/** One emoji from the bundled dataset (emojibase, with GitHub's shortcodes). */
export interface Emoji {
  emoji: string;
  /** Shortcodes without colons, the familiar one first: `sob`, `joy`, `+1`. */
  shortcodes: string[];
  /** The Unicode name, e.g. "face with tears of joy". */
  label: string;
  /** Lower-case search words: the label's words and the dataset's keywords. */
  keywords: string[];
  group: number;
}

export interface EmojiIndex {
  emojis: readonly Emoji[];
  byShortcode: ReadonlyMap<string, Emoji>;
}

/** The picker's categories, in the dataset's group order (group 2, skin-tone components, is left out). */
export const EMOJI_GROUPS: readonly { id: number; name: string; icon: string }[] = [
  { id: 0, name: 'Smileys & emotion', icon: '😀' },
  { id: 1, name: 'People & body', icon: '👋' },
  { id: 3, name: 'Animals & nature', icon: '🐻' },
  { id: 4, name: 'Food & drink', icon: '🍔' },
  { id: 5, name: 'Travel & places', icon: '🚗' },
  { id: 6, name: 'Activities', icon: '⚽' },
  { id: 7, name: 'Objects', icon: '💡' },
  { id: 8, name: 'Symbols', icon: '❤️' },
  { id: 9, name: 'Flags', icon: '🏳️' },
];

let loading: Promise<EmojiIndex> | null = null;

/** The dataset is a lazy chunk of its own: nothing of it is fetched until the first `:` or picker. */
export function loadEmojis(): Promise<EmojiIndex> {
  loading ??= import('./emoji-data').then((m) => m.buildEmojiIndex());
  return loading;
}

/** Shortcode characters: GitHub's names use letters, digits, `_`, `+` and `-` (`+1`, `-1`, `t-rex`). */
const CODE = '[a-z0-9_+\\-]';

/**
 * A shortcode still being typed at the end of the text: a `:` at the start or after whitespace, then
 * at least two code characters. So "10:30", "https://x" and "@Name:so" never open the list.
 */
const TRAILING_SHORTCODE = new RegExp(`(^|\\s):(${CODE}{2,})$`, 'i');

/** Where the `:query` being typed at the end of `text` starts, and the query; null when there is none. */
export function trailingShortcode(
  text: string,
  mentions: readonly MentionRange[] = [],
): { start: number; query: string } | null {
  const match = TRAILING_SHORTCODE.exec(text);
  if (!match) return null;
  const start = match.index + match[1].length;
  // Never inside a chip (a name may hold a space and a colon).
  if (mentions.some((m) => start >= m.start && start < m.end)) return null;
  return { start, query: match[2].toLowerCase() };
}

/**
 * The emoji for `query`, best first: an exact shortcode, then shortcodes starting with it, then
 * shortcodes with a word starting with it (`face` in `clown_face`), then keywords and name words.
 * Otherwise the dataset's order holds (it is stable), so the common faces come first.
 */
export function searchEmojis(index: EmojiIndex, query: string, limit = Infinity): Emoji[] {
  const q = query.trim().toLowerCase().replace(/^:|:$/g, '');
  if (!q) return [];
  const ranks: Emoji[][] = [[], [], [], []];
  for (const e of index.emojis) {
    const rank = rankOf(e, q);
    if (rank >= 0) ranks[rank].push(e);
  }
  // Among prefixes, the closest match first: ":fir" is fire before firefighter.
  const length = (e: Emoji) => Math.min(...e.shortcodes.filter((c) => c.startsWith(q)).map((c) => c.length));
  ranks[1].sort((a, b) => length(a) - length(b));
  return ranks.flat().slice(0, limit);
}

function rankOf(e: Emoji, q: string): number {
  if (e.shortcodes.includes(q)) return 0;
  if (e.shortcodes.some((c) => c.startsWith(q))) return 1;
  if (e.shortcodes.some((c) => c.split(/[_-]/).some((w) => w.startsWith(q)))) return 2;
  if (e.keywords.some((k) => k.startsWith(q))) return 3;
  return -1;
}

/** A complete `:code:` whose opening colon does not follow a letter or digit ("a:b:c" stays). */
const COMPLETE_SHORTCODE = new RegExp(`(?<![\\p{L}\\p{N}]):(${CODE}+):`, 'giu');

/**
 * Turns the known `:code:`s in `text` into their emoji, leaving unknown ones, those inside a chip, and
 * those that end outside `from`..`to` (only what was just typed or pasted converts). Returns the
 * changed span and its replacement, or null when nothing converts.
 */
export function convertShortcodes(
  text: string,
  index: EmojiIndex,
  from = 0,
  to = text.length,
  mentions: readonly MentionRange[] = [],
): { start: number; end: number; text: string } | null {
  let start = -1;
  let end = -1;
  let out = '';
  for (const match of text.matchAll(COMPLETE_SHORTCODE)) {
    const at = match.index;
    const stop = at + match[0].length;
    if (stop <= from || stop > to) continue;
    if (mentions.some((m) => at < m.end && stop > m.start)) continue;
    const emoji = index.byShortcode.get(match[1].toLowerCase());
    if (!emoji) continue;
    if (start < 0) start = at;
    else out += text.slice(end, at);
    out += emoji.emoji;
    end = stop;
  }
  return start < 0 ? null : { start, end, text: out };
}

const RECENT_KEY = 'nd.emoji.recent';
const RECENT_MAX = 8;

/** The emoji this browser used last, newest first. Storage may be unavailable; then there are none. */
export function recentEmojis(): string[] {
  try {
    const stored = JSON.parse(localStorage.getItem(RECENT_KEY) ?? '[]') as unknown;
    return Array.isArray(stored) ? stored.filter((e): e is string => typeof e === 'string').slice(0, RECENT_MAX) : [];
  } catch {
    return [];
  }
}

/** Puts `emoji` at the front of the recent row. */
export function rememberEmoji(emoji: string): string[] {
  const next = [emoji, ...recentEmojis().filter((e) => e !== emoji)].slice(0, RECENT_MAX);
  try {
    localStorage.setItem(RECENT_KEY, JSON.stringify(next));
  } catch {
    // Private mode or full storage: the row just doesn't persist.
  }
  return next;
}
