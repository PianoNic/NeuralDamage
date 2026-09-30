import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { HlmToaster } from '@spartan-ng/helm/sonner';
import { Theme } from './core/theme';

/** The root: the routed page and the one toaster, top centre, clear of the composer. */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, HlmToaster],
  template: `
    <router-outlet />
    <hlm-toaster position="top-center" [theme]="theme.resolved() === 'dark' ? 'dark' : 'light'" />
  `,
})
export class App {
  // Created here so the stored colour scheme applies before the first screen renders.
  protected readonly theme = inject(Theme);
}
