import { Injectable, inject, signal, computed } from '@angular/core';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { AuthService as ApiAuthService } from '@app/api/api/auth.service';
import { UserDto } from '@app/api';
import { firstValueFrom } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly oidc = inject(OidcSecurityService);
  private readonly apiAuth = inject(ApiAuthService);

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
        const user = (await firstValueFrom(
          this.apiAuth.syncUser('body', false, { httpHeaderAccept: 'application/json' }),
        )) as UserDto;
        this._user.set(user);
      }
    } catch {
      this._isAuthenticated.set(false);
    }

    this._isLoading.set(false);
  }

  login() {
    this.oidc.authorize();
  }

  logout() {
    this._user.set(null);
    this._isAuthenticated.set(false);
    this.oidc.logoff().subscribe();
  }
}
