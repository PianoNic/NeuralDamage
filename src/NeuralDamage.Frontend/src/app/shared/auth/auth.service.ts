import { Injectable, inject, signal, computed } from '@angular/core';
import { Router } from '@angular/router';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { SignalRService } from '@app/shared/signalr/signalr.service';
import { UserService as ApiUserService } from '@app/api/api/user.service';
import { UserDto } from '@app/api';
import { firstValueFrom } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly oidc = inject(OidcSecurityService);
  private readonly apiUser = inject(ApiUserService);
  private readonly router = inject(Router);
  private readonly signalr = inject(SignalRService);

  private readonly _user = signal<UserDto | null>(null);
  private readonly _isAuthenticated = signal(false);
  private readonly _isLoading = signal(true);

  readonly user = this._user.asReadonly();
  readonly isAuthenticated = this._isAuthenticated.asReadonly();
  readonly isLoading = this._isLoading.asReadonly();
  readonly displayName = computed(() => this._user()?.displayName ?? '');
  readonly avatarUrl = computed(() => this._user()?.avatarUrl ?? null);

  async checkAuth() {
    try {
      const result = await firstValueFrom(this.oidc.checkAuth());
      this._isAuthenticated.set(result.isAuthenticated);

      if (result.isAuthenticated) {
        // Provisioning happens during token validation, so this only reads.
        const user = (await firstValueFrom(
          this.apiUser.getCurrentUser('body', false, { httpHeaderAccept: 'application/json' }),
        ));
        this._user.set(user);
      }
    } catch {
      this._isAuthenticated.set(false);
    }

    this._isLoading.set(false);
  }

  /**
   * Tells this app's other tabs about a sign-out. The OIDC session lives in
   * per-tab sessionStorage, so without this they would carry on signed in.
   */
  private readonly channel =
    typeof BroadcastChannel === 'undefined' ? null : new BroadcastChannel('neuraldamage-auth');

  constructor() {
    if (this.channel) {
      this.channel.onmessage = (event: MessageEvent) => {
        if (event.data === 'logout' && this._isAuthenticated()) this.logoutLocally();
      };
    }
  }

  login() {
    this.oidc.authorize();
  }

  logout() {
    this._user.set(null);
    this._isAuthenticated.set(false);
    this.channel?.postMessage('logout');
    this.oidc.logoff().subscribe();
  }

  private logoutLocally() {
    this._user.set(null);
    this._isAuthenticated.set(false);
    this.oidc.logoffLocal();
    void this.signalr.stop();
    void this.router.navigate(['/login']);
  }
}
