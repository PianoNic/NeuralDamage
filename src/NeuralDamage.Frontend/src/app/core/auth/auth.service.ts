import { computed, inject, Service, signal } from '@angular/core';
import { Router } from '@angular/router';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { firstValueFrom } from 'rxjs';
import type { UserDto } from '../../api/model/userDto';
import { UserService } from '../../api/api/user.service';
import { clearOidcSession } from './oidc';

/**
 * Sign-in through the identity provider configured on the server (`/api/app`), and the signed-in
 * account as the API sees it.
 */
@Service()
export class AuthService {
  private readonly oidc = inject(OidcSecurityService);
  private readonly users = inject(UserService);
  private readonly router = inject(Router);

  private readonly _user = signal<UserDto | null>(null);
  private readonly _isAuthenticated = signal(false);
  private readonly _isLoading = signal(true);

  readonly user = this._user.asReadonly();
  readonly isAuthenticated = this._isAuthenticated.asReadonly();
  readonly isLoading = this._isLoading.asReadonly();
  readonly displayName = computed(() => this._user()?.displayName || this._user()?.email || '');

  /**
   * Tells this app's other tabs about a sign-out. The OIDC session lives in per-tab sessionStorage,
   * so without this they would carry on signed in.
   */
  private readonly channel =
    typeof BroadcastChannel === 'undefined' ? null : new BroadcastChannel('neuraldamage-auth');

  private checking: Promise<void> | null = null;

  constructor() {
    if (this.channel) {
      this.channel.onmessage = (event: MessageEvent) => {
        if (event.data === 'logout' && this._isAuthenticated()) this.logoutLocally();
      };
    }
  }

  /** Completes a sign-in on return from the provider, or restores the session in this tab. */
  checkAuth(): Promise<void> {
    this.checking ??= this.performCheck().finally(() => (this.checking = null));
    return this.checking;
  }

  private async performCheck(): Promise<void> {
    try {
      const result = await firstValueFrom(this.oidc.checkAuth());
      this._isAuthenticated.set(result.isAuthenticated);

      if (result.isAuthenticated) {
        // Provisioning happens during token validation, so this only reads.
        this._user.set(await firstValueFrom(this.users.getCurrentUser()));
      }
    } catch {
      // A session the provider no longer honours (a changed issuer, a revoked client) must not
      // block signing in again: drop it and start clean.
      clearOidcSession();
      this._isAuthenticated.set(false);
      this._user.set(null);
    }

    this._isLoading.set(false);
  }

  login(): void {
    this.oidc.authorize();
  }

  logout(): void {
    this._user.set(null);
    this._isAuthenticated.set(false);
    this.channel?.postMessage('logout');
    this.oidc.logoff().subscribe();
  }

  private logoutLocally(): void {
    this._user.set(null);
    this._isAuthenticated.set(false);
    this.oidc.logoffLocal();
    void this.router.navigate(['/login']);
  }
}
