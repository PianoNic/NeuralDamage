// The dataset itself: only ever reached through loadEmojis()'s dynamic import, so it is a lazy chunk.
import compact from 'emojibase-data/en/compact.json';
import github from 'emojibase-data/en/shortcodes/github.json';
import { Emoji, EmojiIndex } from './emoji';

interface CompactEmoji {
  unicode: string;
  hexcode: string;
  label: string;
  tags?: string[];
  group?: number;
  order?: number;
}

export function buildEmojiIndex(): EmojiIndex {
  const codes = github as Record<string, string | string[]>;
  const emojis: Emoji[] = [];
  const byShortcode = new Map<string, Emoji>();
  const sorted = [...(compact as CompactEmoji[])].sort((a, b) => (a.order ?? 0) - (b.order ?? 0));
  for (const e of sorted) {
    const shortcodes = ([] as string[]).concat(codes[e.hexcode] ?? []);
    // Skin-tone swatches (group 2) and the regional letters have no shortcode and no place in a chat.
    if (e.group === undefined || e.group === 2 || !shortcodes.length) continue;
    const keywords = [...new Set([...e.label.toLowerCase().split(/[\s:,]+/), ...(e.tags ?? [])])].filter(Boolean);
    const emoji: Emoji = { emoji: e.unicode, shortcodes, label: e.label, keywords, group: e.group };
    emojis.push(emoji);
    for (const code of shortcodes) if (!byShortcode.has(code)) byShortcode.set(code, emoji);
  }
  return { emojis, byShortcode };
}
