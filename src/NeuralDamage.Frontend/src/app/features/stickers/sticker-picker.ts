import { NgTemplateOutlet } from '@angular/common';
import { Component, ElementRef, inject, OnInit, output, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideSearch } from '@ng-icons/lucide';
import { HlmInputGroupImports } from '@spartan-ng/helm/input-group';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { StickerDto } from '../../core/models';
import { InView } from '../../shared/in-view';
import { scrollToTopOnSearch, StickerFeed } from './sticker-feed';
import { StickerImage } from './sticker-image';
import { StickerLibrary } from './sticker-library';

/**
 * The composer's sticker panel, as in WhatsApp: a Recent row, then the library a page at a time,
 * loading more while scrolling, with a search the server runs over names and descriptions.
 * Picking one sends it straight away.
 */
@Component({
  selector: 'app-sticker-picker',
  imports: [NgTemplateOutlet, RouterLink, NgIcon, HlmInputGroupImports, HlmSpinner, StickerImage, InView],
  providers: [StickerFeed, provideIcons({ lucideSearch })],
  host: { class: 'flex w-80 max-w-[calc(100vw-2rem)] flex-col gap-2' },
  template: `
    <div hlmInputGroup>
      <input
        hlmInputGroupInput
        type="search"
        placeholder="Search stickers"
        aria-label="Search stickers"
        [value]="feed.query()"
        (input)="feed.search($any($event.target).value)"
      />
      <div hlmInputGroupAddon><ng-icon name="lucideSearch" /></div>
    </div>

    <div #scroller class="max-h-80 min-h-24 overflow-y-auto" data-testid="picker-scroll">
      @if (!feed.ready()) {
        <div class="flex justify-center py-8"><hlm-spinner /></div>
      } @else if (!feed.searched() && !feed.items().length) {
        <p class="text-muted-foreground px-2 py-6 text-center text-sm">
          No stickers yet. Add some on the
          <a routerLink="/stickers" class="text-foreground underline underline-offset-2">Stickers</a> page.
        </p>
      } @else {
        @if (!feed.searched() && library.recent().length) {
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
          @if (!feed.searched() && library.recent().length) {
            <h3 class="text-muted-foreground px-1 pb-1 text-xs font-medium">All stickers</h3>
          }
          @if (feed.items().length) {
            <div class="grid grid-cols-4 gap-1" data-testid="all">
              @for (sticker of feed.items(); track sticker.id) {
                <ng-container *ngTemplateOutlet="cell; context: { $implicit: sticker }" />
              }
            </div>
            @if (feed.hasMore() && !feed.loading()) {
              <div class="h-1" aria-hidden="true" data-testid="more" [inViewRoot]="scroller" (appInView)="feed.more()"></div>
            } @else if (feed.loading()) {
              <div class="flex justify-center py-2"><hlm-spinner class="size-4" /></div>
            }
          } @else if (feed.loading()) {
            <div class="flex justify-center py-6"><hlm-spinner /></div>
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
  protected readonly feed = inject(StickerFeed);
  readonly pick = output<StickerDto>();
  private readonly scroller = viewChild<ElementRef<HTMLElement>>('scroller');

  constructor() {
    scrollToTopOnSearch(this.feed, this.scroller);
  }

  ngOnInit(): void {
    this.feed.pageSize = 48;
    void this.library.load();
    void this.feed.reset();
  }
}
