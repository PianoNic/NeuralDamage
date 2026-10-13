import { afterNextRender, Component, computed, ElementRef, output, signal, viewChild } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideClock, lucideSearch } from '@ng-icons/lucide';
import { HlmInputGroupImports } from '@spartan-ng/helm/input-group';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { Emoji, EMOJI_GROUPS, EmojiIndex, loadEmojis, recentEmojis, rememberEmoji, searchEmojis } from './emoji';

/** Emoji per grid row; the panel is sized to fit exactly this many cells. */
export const EMOJI_COLUMNS = 8;
/** Every row, header or emoji, is this tall, so the list can be virtualised with a fixed size. */
const ROW_PX = 36;
/** The grid's height: eight rows. */
const VIEW_PX = 288;
/** Rows rendered beyond each edge, so a fast scroll does not show blanks. */
const OVERSCAN = 6;

export type PickerRow =
  | { kind: 'header'; key: string; label: string; group: number | 'recent' | 'search' }
  | { kind: 'emojis'; key: string; emojis: Emoji[] };

/**
 * The composer's emoji panel: a search over shortcodes and keywords, a tab per category, a Recent row
 * kept in this browser, and the whole set as one virtualised grid, so only the visible rows render.
 */
@Component({
  selector: 'app-emoji-picker',
  imports: [NgIcon, HlmInputGroupImports, HlmSpinner],
  providers: [provideIcons({ lucideClock, lucideSearch })],
  host: { class: 'flex w-[19.5rem] max-w-[calc(100vw-2rem)] flex-col gap-2' },
  template: `
    <div hlmInputGroup>
      <input
        #search
        hlmInputGroupInput
        type="search"
        placeholder="Search emoji"
        aria-label="Search emoji"
        [value]="query()"
        (input)="setQuery($any($event.target).value)"
        (keydown.enter)="pickFirst($event)"
      />
      <div hlmInputGroupAddon><ng-icon name="lucideSearch" /></div>
    </div>

    @if (!query()) {
      <div class="flex justify-between" role="toolbar" aria-label="Emoji categories">
        @if (recent().length) {
          <button
            type="button"
            class="hover:bg-accent text-muted-foreground aria-pressed:bg-muted aria-pressed:text-foreground flex size-7 items-center justify-center rounded-md"
            aria-label="Recent"
            title="Recent"
            [attr.aria-pressed]="activeGroup() === 'recent'"
            (click)="jumpTo('recent')"
          >
            <ng-icon name="lucideClock" />
          </button>
        }
        @for (group of groups; track group.id) {
          <button
            type="button"
            class="hover:bg-accent aria-pressed:bg-muted flex size-7 items-center justify-center rounded-md text-base grayscale aria-pressed:grayscale-0 hover:grayscale-0"
            [attr.aria-label]="group.name"
            [title]="group.name"
            [attr.aria-pressed]="activeGroup() === group.id"
            (click)="jumpTo(group.id)"
          >
            {{ group.icon }}
          </button>
        }
      </div>
    }

    @if (!index()) {
      <div class="flex h-72 items-center justify-center"><hlm-spinner /></div>
    } @else if (!rows().length) {
      <p class="text-muted-foreground flex h-72 items-center justify-center text-sm">No emoji found</p>
    } @else {
      <!-- Only the rows in view (and a few either side) exist; the spacer keeps the scrollbar honest. -->
      <div #scroller class="h-72 overflow-y-auto [scrollbar-width:thin]" data-testid="emoji-grid" (scroll)="onScroll()">
        <div class="relative" [style.height.px]="rows().length * rowPx">
          <div class="absolute inset-x-0 top-0" [style.transform]="'translateY(' + window().start * rowPx + 'px)'">
            @for (row of window().rows; track row.key) {
              @if (row.kind === 'header') {
                <h3 class="text-muted-foreground flex h-9 items-end px-1 pb-1.5 text-xs font-medium">{{ row.label }}</h3>
              } @else {
                <div class="grid h-9 grid-cols-8" role="group">
                  @for (e of row.emojis; track e.emoji) {
                    <button
                      type="button"
                      class="hover:bg-accent focus-visible:ring-ring/50 flex size-9 items-center justify-center rounded-md text-xl outline-none focus-visible:ring-2"
                      [attr.aria-label]="e.label"
                      [title]="':' + e.shortcodes[0] + ':'"
                      (mouseenter)="hovered.set(e)"
                      (focus)="hovered.set(e)"
                      (click)="choose(e)"
                    >
                      {{ e.emoji }}
                    </button>
                  }
                </div>
              }
            }
          </div>
        </div>
      </div>
    }

    <div class="text-muted-foreground flex h-7 items-center gap-2 border-t px-1 pt-1.5 text-xs" aria-live="polite">
      @if (hovered(); as e) {
        <span class="text-lg leading-none">{{ e.emoji }}</span>
        <span class="text-foreground truncate font-medium">:{{ e.shortcodes[0] }}:</span>
      } @else {
        <span>Tip: type <span class="text-foreground font-medium">:sob</span> in a message</span>
      }
    </div>
  `,
})
export class EmojiPicker {
  readonly pick = output<string>();

  protected readonly groups = EMOJI_GROUPS;
  protected readonly rowPx = ROW_PX;
  readonly index = signal<EmojiIndex | null>(null);
  readonly query = signal('');
  readonly recent = signal(recentEmojis());
  protected readonly hovered = signal<Emoji | null>(null);
  protected readonly activeGroup = signal<number | 'recent' | null>(null);
  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');
  private readonly scrollTop = signal(0);
  private readonly search = viewChild.required<ElementRef<HTMLInputElement>>('search');

  private readonly byChar = computed(() => new Map(this.index()?.emojis.map((e) => [e.emoji, e])));

  /** The grid as rows: a header, then its emoji eight to a row; search results replace it all. */
  readonly rows = computed<PickerRow[]>(() => {
    const index = this.index();
    if (!index) return [];
    const query = this.query().trim();
    if (query) {
      const found = searchEmojis(index, query);
      return found.length ? [header('search', 'Search results'), ...gridRows('search', found)] : [];
    }
    const rows: PickerRow[] = [];
    const recent = this.recent().flatMap((c) => this.byChar().get(c) ?? []);
    if (recent.length) rows.push(header('recent', 'Recent'), ...gridRows('recent', recent));
    for (const group of EMOJI_GROUPS) {
      const emojis = index.emojis.filter((e) => e.group === group.id);
      if (emojis.length) rows.push(header(group.id, group.name), ...gridRows(String(group.id), emojis));
    }
    return rows;
  });

  /** The rows to render for the current scroll position. */
  protected readonly window = computed(() => {
    const first = Math.floor(this.scrollTop() / ROW_PX);
    const start = Math.max(0, first - OVERSCAN);
    const end = first + Math.ceil(VIEW_PX / ROW_PX) + OVERSCAN;
    return { start, rows: this.rows().slice(start, end) };
  });

  constructor() {
    void loadEmojis().then((index) => {
      this.index.set(index);
      this.activeGroup.set(this.recent().length ? 'recent' : EMOJI_GROUPS[0].id);
    });
    afterNextRender(() => this.search().nativeElement.focus());
  }

  setQuery(value: string): void {
    this.query.set(value);
    this.scrollTo(0);
  }

  private scrollTo(px: number): void {
    const el = this.scroller()?.nativeElement;
    if (el) el.scrollTop = px;
    this.scrollTop.set(px);
  }

  choose(emoji: Emoji): void {
    this.recent.set(rememberEmoji(emoji.emoji));
    this.pick.emit(emoji.emoji);
  }

  protected pickFirst(event: Event): void {
    event.preventDefault();
    const first = this.rows().find((r) => r.kind === 'emojis');
    if (first?.kind === 'emojis') this.choose(first.emojis[0]);
  }

  protected jumpTo(group: number | 'recent'): void {
    const at = this.rows().findIndex((r) => r.kind === 'header' && r.group === group);
    if (at < 0) return;
    this.scrollTo(at * ROW_PX);
    this.activeGroup.set(group);
  }

  /** Renders the rows now in view, and lights up the tab of the section at the top. */
  protected onScroll(): void {
    const top = this.scroller()?.nativeElement.scrollTop ?? 0;
    this.scrollTop.set(top);
    const first = Math.floor(top / ROW_PX);
    const rows = this.rows();
    for (let i = Math.min(first, rows.length - 1); i >= 0; i--) {
      const row = rows[i];
      if (row.kind === 'header') {
        if (row.group !== 'search') this.activeGroup.set(row.group);
        return;
      }
    }
  }
}

function header(group: number | 'recent' | 'search', label: string): PickerRow {
  return { kind: 'header', key: `h-${group}`, label, group };
}

function gridRows(key: string, emojis: Emoji[]): PickerRow[] {
  const rows: PickerRow[] = [];
  for (let i = 0; i < emojis.length; i += EMOJI_COLUMNS) {
    rows.push({ kind: 'emojis', key: `${key}-${i}`, emojis: emojis.slice(i, i + EMOJI_COLUMNS) });
  }
  return rows;
}
