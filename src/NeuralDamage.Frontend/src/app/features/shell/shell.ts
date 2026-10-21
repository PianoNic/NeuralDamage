import { Component, inject, OnDestroy } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideWifiOff } from '@ng-icons/lucide';
import { HlmSidebarImports } from '@spartan-ng/helm/sidebar';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { SignalRService } from '../../core/signalr/signalr.service';
import { AppSidebar } from './app-sidebar';

/**
 * The signed-in frame: helm's inset sidebar on the page, and the content in a rounded panel beside
 * it. A thin banner across the panel's top says when live updates are down.
 */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, NgIcon, HlmSidebarImports, HlmSpinner, AppSidebar],
  providers: [provideIcons({ lucideWifiOff })],
  template: `
    <app-sidebar>
      <main
        hlmSidebarInset
        class="h-svh min-h-0 overflow-hidden md:h-[calc(100svh-1rem)]"
      >
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
        <router-outlet />
      </main>
    </app-sidebar>
  `,
})
export class Shell implements OnDestroy {
  protected readonly signalr = inject(SignalRService);
  protected readonly bannerClass =
    'bg-muted text-muted-foreground flex shrink-0 items-center justify-center gap-2 border-b px-3 py-1.5 text-xs md:rounded-t-2xl';

  /** Leaving the signed-in frame (a sign-out in another tab) drops the live connections. */
  ngOnDestroy(): void {
    void this.signalr.stop();
  }
}
