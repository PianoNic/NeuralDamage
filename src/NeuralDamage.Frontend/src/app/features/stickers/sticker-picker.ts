import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject, OnInit, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideSearch } from '@ng-icons/lucide';
import { HlmInputGroupImports } from '@spartan-ng/helm/input-group';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { StickerDto } from '../../core/models';
import { StickerImage } from './sticker-image';
import { matchesSticker, StickerLibrary } from './sticker-library';

/**
 * The composer's sticker panel, as in WhatsApp: a Recent row, then the whole library, with a
 * search over the descriptions. Picking one sends it straight away.
 */
@Component({
  selector: 'app-sticker-picker',
  imports: [NgTemplateOutlet, RouterLink, NgIcon, HlmInputGroupImports, HlmSpinner, StickerImage],
  providers: [provideIcons({ lucideSearch })],
  host: { class: 'flex w-80 max-w-[calc(100vw-2rem)] flex-col gap-2' },
  template: `
    <div hlmInputGroup>
      <input
        hlmInputGroupInput
        type="search"
        placeholder="Search stickers"
        aria-label="Search stickers"
        [value]="query()"
        (input)="query.set($any($event.target).value)"
      />
      <div hlmInputGroupAddon><ng-icon name="lucideSearch" /></div>
    </div>

    <div class="max-h-80 min-h-24 overflow-y-auto">
      @if (library.loading() && !library.stickers().length) {
        <div class="flex justify-center py-8"><hlm-spinner /></div>
      } @else if (!library.stickers().length) {
        <p class="text-muted-foreground px-2 py-6 text-center text-sm">
          No stickers yet. Add some on the
          <a routerLink="/stickers" class="text-foreground underline underline-offset-2">Stickers</a> page.
        </p>
      } @else {
        @if (!query().trim() && library.recent().length) {
          <section aria-label="Recent">
            <h3 class="text-muted-foreground px-1 pb-1 text-xs font-medium">Recent</h3>
            <div class="grid grid-cols-4 gap-1 pb-2" data-testid="recent">
              @for (sticker of library.recent().slice(0, 8); track sticker.id) {
                <ng-container *ngTemplateOutlet="cell; context: { $implicit: sticker }" />
              }
            </div>
          </section>
        }
        <section aria-label="All stickers">
          @if (!query().trim() && library.recent().length) {
            <h3 class="text-muted-foreground px-1 pb-1 text-xs font-medium">All stickers</h3>
          }
          @if (visible().length) {
            <div class="grid grid-cols-4 gap-1" data-testid="all">
              @for (sticker of visible(); track sticker.id) {
                <ng-container *ngTemplateOutlet="cell; context: { $implicit: sticker }" />
              }
            </div>
          } @else {
            <p class="text-muted-foreground px-2 py-6 text-center text-sm">No sticker matches.</p>
          }
        </section>
      }
    </div>

    <ng-template #cell let-sticker>
      <button
        type="button"
        class="hover:bg-accent focus-visible:ring-ring/50 flex aspect-square items-center justify-center rounded-lg p-1 outline-none focus-visible:ring-2"
        [attr.aria-label]="'Send sticker ' + sticker.name"
        [title]="sticker.description || sticker.name"
        (click)="pick.emit(sticker)"
      >
        <app-sticker-image [url]="sticker.url" [alt]="sticker.description || sticker.name" [px]="64" />
      </button>
    </ng-template>
  `,
})
export class StickerPicker implements OnInit {
  protected readonly library = inject(StickerLibrary);
  readonly pick = output<StickerDto>();

  protected readonly query = signal('');
  protected readonly visible = computed(() => {
    const query = this.query();
    return this.library.stickers().filter((s) => matchesSticker(s, query));
  });

  ngOnInit(): void {
    void this.library.load();
  }
}
