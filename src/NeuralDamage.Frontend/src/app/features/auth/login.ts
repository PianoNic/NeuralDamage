import { Component, inject, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { HlmSpinner } from '@spartan-ng/helm/spinner';
import { AuthService } from '../../core/auth/auth.service';

/**
 * Sign in: there is only one way in, so this goes straight on to the identity provider. A failed
 * sign-in comes back to the callback page, which says so, so this never loops.
 */
@Component({
  selector: 'app-login',
  imports: [HlmSpinner],
  host: { class: 'bg-background flex min-h-svh items-center justify-center p-4' },
  template: `<hlm-spinner class="text-muted-foreground" />`,
})
export class Login implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  async ngOnInit(): Promise<void> {
    if (this.auth.isLoading()) await this.auth.checkAuth();
    if (this.auth.isAuthenticated()) void this.router.navigate(['/']);
    else this.auth.login();
  }
}