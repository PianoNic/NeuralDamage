import { Component, computed, input } from '@angular/core';
import { findMentions } from '../chat/mentions';

export interface TextPart {
  text: string;
  mention: boolean;
}

/** Plain text cut into runs, with every @mention of a known name as a run of its own. */
export function textParts(text: string, names: readonly string[]): TextPart[] {
  const parts: TextPart[] = [];
  let at = 0;
  for (const m of findMentions(text, names)) {
    if (m.start > at) parts.push({ text: text.slice(at, m.start), mention: false });
    parts.push({ text: text.slice(m.start, m.end), mention: true });
    at = m.end;
  }
  if (at < text.length) parts.push({ text: text.slice(at), mention: false });
  return parts;
}

/**
 * A post's or comment's text: plain, line breaks kept, with @mentions of bots and people as the
 * same tinted chips the chat shows.
 */
@Component({
  selector: 'app-feed-text',
  host: { class: 'block whitespace-pre-wrap wrap-anywhere' },
  template: `@for (part of parts(); track $index) {@if (part.mention) {<span
          data-testid="mention"
          class="rounded-[0.3em] bg-[color-mix(in_oklab,currentColor_14%,transparent)] px-[0.2em] font-medium [box-decoration-break:clone]"
          >{{ part.text }}</span
        >} @else {{{ part.text }}}}`,
})
export class FeedText {
  readonly text = input.required<string>();
  readonly names = input<readonly string[]>([]);

  protected readonly parts = computed(() => textParts(this.text(), this.names()));
}
