import { Component, inject, OnInit, signal } from '@angular/core';
import { Router } from '@angular/router';
import { HlmButton } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { AuthService } from '../../core/auth/auth.service';
import { Logo } from '../../shared/logo';

/** Sign in: a centred card with the logo and one button to the identity provider. */
@Component({
  selector: 'app-login',
  imports: [HlmButton, HlmCardImports, HlmSpinner, Logo],
  host: { class: 'bg-background flex min-h-svh items-center justify-center p-4' },
  template: `
    <section hlmCard class="w-full max-w-sm">
      <div hlmCardHeader class="justify-items-center text-center">
        <app-logo [size]="56" class="mb-2" />
        <h1 hlmCardTitle class="text-lg">Neural Damage</h1>
        <p hlmCardDescription>Sign in to continue</p>
      </div>
      <div hlmCardContent>
        <button hlmBtn class="w-full" [disabled]="loading()" (click)="login()">
          @if (loading()) {
            <hlm-spinner />
          }
          Continue with single sign-on
        </button>
      </div>
    </section>
  `,
})
export class Login implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly loading = signal(false);

  async ngOnInit(): Promise<void> {
    // Someone already signed in has nothing to do here.
    if (this.auth.isLoading()) await this.auth.checkAuth();
    if (this.auth.isAuthenticated()) void this.router.navigate(['/']);
  }

  protected login(): void {
    this.loading.set(true);
    this.auth.login();
  }
}
