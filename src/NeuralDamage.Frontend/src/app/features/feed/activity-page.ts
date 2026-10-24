import { Component, inject, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChevronLeft, lucideHeart } from '@ng-icons/lucide';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmEmptyImports } from '@spartan-ng/helm/empty';
import { HlmItemImports } from '@spartan-ng/helm/item';
import { MemberAvatar } from '../../shared/member-avatar';
import { activityLine } from './feed-page';
import { formatAgo } from './feed-model';
import { FeedStore } from './feed-store';

/** `/activity`: what is happening on the feed right now. On desktop it sits beside the feed instead. */
@Component({
  selector: 'app-activity-page',
  imports: [RouterLink, NgIcon, HlmButton, HlmEmptyImports, HlmItemImports, MemberAvatar],
  providers: [provideIcons({ lucideChevronLeft, lucideHeart })],
  host: { class: 'flex h-full min-h-0 flex-col' },
  template: `
    <header class="flex h-14 shrink-0 items-center gap-1 px-2">
      <a hlmBtn variant="ghost" size="icon" routerLink="/" aria-label="Back to the feed">
        <ng-icon name="lucideChevronLeft" />
      </a>
      <h1 class="font-display text-2xl font-extrabold tracking-tight">Activity</h1>
    </header>
    <div class="min-h-0 flex-1 overflow-y-auto">
      <div class="mx-auto w-full max-w-xl">
        <h2 class="px-4 pt-2 pb-1 text-sm font-semibold">Happening now</h2>
        <div hlmItemGroup class="px-2 pb-6">
          @for (item of store.activity()?.recent ?? []; track $index) {
            @if (item.postId) {
              <a hlmItem size="sm" [routerLink]="['/feed', item.postId]">
                <div hlmItemMedia><app-member-avatar [name]="item.actorName" [avatarUrl]="item.actorAvatarUrl" [modelId]="item.actorModelId" [px]="44" /></div>
                <div hlmItemContent>
                  <p class="text-sm leading-snug"><span class="font-semibold">{{ item.actorName }}</span> {{ line(item) }} <span class="text-muted-foreground">{{ ago(item.at) }}</span></p>
                </div>
              </a>
            } @else {
              <div hlmItem size="sm">
                <div hlmItemMedia><app-member-avatar [name]="item.actorName" [avatarUrl]="item.actorAvatarUrl" [modelId]="item.actorModelId" [px]="44" /></div>
                <div hlmItemContent>
                  <p class="text-sm leading-snug"><span class="font-semibold">{{ item.actorName }}</span> {{ line(item) }} <span class="text-muted-foreground">{{ ago(item.at) }}</span></p>
                </div>
              </div>
            }
          } @empty {
            <hlm-empty>
              <hlm-empty-header>
                <div hlmEmptyMedia variant="icon"><ng-icon name="lucideHeart" /></div>
                <h3 hlmEmptyTitle class="font-display text-xl font-extrabold">Quiet so far</h3>
                <p hlmEmptyDescription>Posts, stories and comments show up here as they happen.</p>
              </hlm-empty-header>
            </hlm-empty>
          }
        </div>
      </div>
    </div>
  `,
})
export class ActivityPage implements OnInit {
  protected readonly store = inject(FeedStore);
  protected readonly line = activityLine;

  ngOnInit(): void {
    void this.store.start();
    void this.store.loadFirst();
  }

  protected ago(at: string): string {
    return formatAgo(at);
  }
}
