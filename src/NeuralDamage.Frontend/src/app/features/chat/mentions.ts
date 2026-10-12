/** A name that @mentions a chat member: their display name, or one of a bot's nicknames. */
export interface MentionTarget {
  name: string;
  memberId: string;
}

/** A mention found in text: `start` is the "@", `end` is just past the name. */
export interface MentionRange {
  start: number;
  end: number;
  name: string;
}

/** A bot's comma-separated nicknames, as the server splits them. */
export function nicknames(aliases: string | null | undefined): string[] {
  return (aliases ?? '').split(',').map((a) => a.trim()).filter(Boolean);
}

/** What .NET's `\w` matches, so a mention here is a mention to the bot decision engine. */
const WORD = '\\p{L}\\p{Mn}\\p{Nd}\\p{Pc}';

/**
 * "@Name" as a whole word, case-insensitive: the backend's `(?<!\w)@Name(?!\w)`. Longer names win
 * over their prefixes ("@Rex Jr" before "@Rex").
 */
function mentionPattern(names: readonly string[]): RegExp | null {
  const known = [...new Set(names.map((n) => n.trim()).filter(Boolean))].sort((a, b) => b.length - a.length);
  if (!known.length) return null;
  return new RegExp(`(?<![${WORD}])@(${known.map(escapeRegExp).join('|')})(?![${WORD}])`, 'giu');
}

/** Every mention of a known name in plain text, in order. */
export function findMentions(text: string, names: readonly string[]): MentionRange[] {
  const pattern = text.includes('@') ? mentionPattern(names) : null;
  if (!pattern) return [];
  return [...text.matchAll(pattern)].map((m) => ({ start: m.index, end: m.index + m[0].length, name: m[1] }));
}

/** The mention `at` falls strictly inside, if any (its edges are outside). */
export function mentionAround(mentions: readonly MentionRange[], at: number): MentionRange | undefined {
  return mentions.find((m) => at > m.start && at < m.end);
}

/**
 * Marks `@Name` for every member name or nickname so it renders as a mention the reader can open.
 * Code spans and fenced blocks are left alone. The name is escaped; nothing else is added to the HTML.
 */
export function markMentions(markdown: string, targets: readonly MentionTarget[]): string {
  const byName = new Map(targets.map((t) => [t.name.trim().toLowerCase(), t.memberId]));
  const pattern = markdown.includes('@') ? mentionPattern(targets.map((t) => t.name)) : null;
  if (!pattern) return markdown;

  // Odd segments are code: ``` fences or `inline` spans.
  return markdown
    .split(/(```[\s\S]*?```|`[^`\n]*`)/)
    .map((segment, i) =>
      i % 2
        ? segment
        : segment.replace(pattern, (_, name: string) => {
            const id = byName.get(name.toLowerCase()) ?? '';
            return `<span class="mention" data-member-id="${escapeHtml(id)}" role="button" tabindex="0" aria-haspopup="dialog">@${escapeHtml(name)}</span>`;
          }),
    )
    .join('');
}

function escapeRegExp(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function escapeHtml(text: string): string {
  return text.replace(/[&<>"']/g, (c) => `&#${c.charCodeAt(0)};`);
}
