/**
 * Marks `@Name` for every known member name so it renders as a mention. Code spans and fenced
 * blocks are left alone, and longer names win over their prefixes ("@Rex Jr" before "@Rex").
 */
export function markMentions(markdown: string, names: readonly string[]): string {
  const known = [...new Set(names.filter(Boolean))].sort((a, b) => b.length - a.length);
  if (!known.length || !markdown.includes('@')) return markdown;

  const pattern = new RegExp(
    `(^|[^\\w@])@(${known.map(escapeRegExp).join('|')})(?![\\w])`,
    'gi',
  );
  // Odd segments are code: ``` fences or `inline` spans.
  return markdown
    .split(/(```[\s\S]*?```|`[^`\n]*`)/)
    .map((segment, i) =>
      i % 2 ? segment : segment.replace(pattern, (_, lead: string, name: string) =>
        `${lead}<span class="mention">@${escapeHtml(name)}</span>`,
      ),
    )
    .join('');
}

function escapeRegExp(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

function escapeHtml(text: string): string {
  return text.replace(/[&<>"']/g, (c) => `&#${c.charCodeAt(0)};`);
}
