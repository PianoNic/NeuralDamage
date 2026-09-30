import { Component, computed, input } from '@angular/core';
import { MemberAvatar } from '../../shared/member-avatar';

export interface Typer {
  id: string;
  name: string;
  avatarUrl: string | null;
  /** A bot's model, for the vendor icon when it has no picture of its own. */
  modelId?: string | null;
}

/** How many names are spelled out before the rest become "N others". */
const NAMED = 2;
const MAX_AVATARS = 3;

type Part = { text: string; name: boolean };

/**
 * "Rex, Juno and alice are typing" followed by three small dots on the baseline, with everyone's
 * avatars stacked in front. One row for everyone; no bubble.
 */
export function typingParts(names: readonly string[]): Part[] {
  const name = (text: string): Part => ({ text, name: true });
  const plain = (text: string): Part => ({ text, name: false });
  switch (names.length) {
    case 0:
      return [];
    case 1:
      return [name(names[0]), plain(' is typing')];
    case 2:
      return [name(names[0]), plain(' and '), name(names[1]), plain(' are typing')];
    case 3:
      return [
        name(names[0]),
        plain(', '),
        name(names[1]),
        plain(' and '),
        name(names[2]),
        plain(' are typing'),
      ];
    default:
      return [
        ...names.slice(0, NAMED).flatMap((n, i) => (i ? [plain(', '), name(n)] : [name(n)])),
        plain(` and ${names.length - NAMED} others are typing`),
      ];
  }
}

@Component({
  selector: 'app-typing-row',
  imports: [MemberAvatar],
  host: { class: 'block' },
  template: `
    @if (typers().length) {
      <div class="flex h-8 items-center gap-2 text-xs" role="status" aria-live="polite">
        <div class="flex -space-x-1.5">
          @for (typer of avatars(); track typer.id) {
            <span class="ring-background flex rounded-full ring-2" [title]="typer.name">
              <app-member-avatar [name]="typer.name" [avatarUrl]="typer.avatarUrl" [modelId]="typer.modelId" [px]="24" />
            </span>
          }
        </div>
        <p class="text-muted-foreground">
          @for (part of parts(); track $index) {
            @if (part.name) {
              <span class="text-foreground font-medium">{{ part.text }}</span>
            } @else {
              {{ part.text }}
            }
          }
          <span class="ms-1 inline-flex gap-0.5 align-baseline" aria-hidden="true">
            <span class="typing-dot bg-muted-foreground inline-block size-1 rounded-full"></span>
            <span class="typing-dot bg-muted-foreground inline-block size-1 rounded-full [animation-delay:150ms]"></span>
            <span class="typing-dot bg-muted-foreground inline-block size-1 rounded-full [animation-delay:300ms]"></span>
          </span>
        </p>
      </div>
    }
  `,
})
export class TypingRow {
  readonly typers = input.required<readonly Typer[]>();

  protected readonly avatars = computed(() => this.typers().slice(0, MAX_AVATARS));
  protected readonly parts = computed(() => typingParts(this.typers().map((t) => t.name)));
}
