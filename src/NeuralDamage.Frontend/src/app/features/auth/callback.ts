import { Component, inject, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { AuthService } from '../../core/auth/auth.service';

/** Where the identity provider sends the browser back to; finishes the sign-in and moves on. */
@Component({
  selector: 'app-callback',
  imports: [RouterLink, HlmButton, HlmSpinner],
  host: { class: 'bg-background flex min-h-svh items-center justify-center p-4' },
  template: `
    @if (error()) {
      <div class="flex flex-col items-center gap-3 text-center">
        <p class="text-destructive text-sm">{{ error() }}</p>
        <a hlmBtn variant="outline" routerLink="/login">Back to sign in</a>
      </div>
    } @else {
      <div class="text-muted-foreground flex items-center gap-2 text-sm">
        <hlm-spinner />
        Signing in…
      </div>
    }
  `,
})
export class Callback implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly error = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    await this.auth.checkAuth();
    if (this.auth.isAuthenticated()) void this.router.navigate(['/']);
    else this.error.set('Signing in did not work. Please try again.');
  }
}
