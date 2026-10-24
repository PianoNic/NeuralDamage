import { Component, computed, inject, OnDestroy } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideWifiOff } from '@ng-icons/lucide';
import { HlmSidebarImports } from '@spartan-ng/helm/sidebar';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { filter, map } from 'rxjs';
import { SignalRService } from '../../core/signalr/signalr.service';
import { AppSidebar } from './app-sidebar';
import { BottomBar } from './bottom-bar';

/** Pages that take the whole phone screen, like an open chat, hide the bottom bar. */
const FULL_SCREEN = [/^\/chats\/.+/, /^\/feed\/story/];

/**
 * The signed-in frame: the rail on desktop, the bottom bar on phones, and the page between. A thin
 * banner across the top says when live updates are down.
 */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, NgIcon, HlmSidebarImports, HlmSpinner, AppSidebar, BottomBar],
  providers: [provideIcons({ lucideWifiOff })],
  template: `
    <app-sidebar [path]="path()">
      <main hlmSidebarInset class="h-svh min-h-0 overflow-hidden">
        @switch (signalr.state()) {
          @case ('reconnecting') {
            <div role="status" [class]="bannerClass">
              <hlm-spinner class="size-3" aria-hidden="true" />
              Reconnecting…
            </div>
          }
          @case ('offline') {
            <div role="status" [class]="bannerClass">
              <ng-icon name="lucideWifiOff" aria-hidden="true" />
              You're offline. Reconnecting when the network is back.
            </div>
          }
        }
        <div class="grid min-h-0 flex-1 grid-cols-[minmax(0,1fr)] grid-rows-[minmax(0,1fr)] [&>router-outlet]:hidden">
          <router-outlet />
        </div>
        @if (!fullScreen()) {
          <app-bottom-bar class="md:hidden" [path]="path()" />
        }
      </main>
    </app-sidebar>
  `,
})
export class Shell implements OnDestroy {
  protected readonly signalr = inject(SignalRService);
  private readonly router = inject(Router);
  protected readonly bannerClass =
    'bg-muted text-muted-foreground flex shrink-0 items-center justify-center gap-2 border-b px-3 py-1.5 text-xs';

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => this.router.url),
    ),
    { initialValue: this.router.url },
  );
  protected readonly path = computed(() => this.url().split(/[?#]/)[0]);
  protected readonly fullScreen = computed(() => FULL_SCREEN.some((pattern) => pattern.test(this.path())));

  /** Leaving the signed-in frame (a sign-out in another tab) drops the live connections. */
  ngOnDestroy(): void {
    void this.signalr.stop();
  }
}
