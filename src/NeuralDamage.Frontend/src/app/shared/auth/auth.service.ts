import { Injectable, inject, signal, computed } from '@angular/core';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { UserService as ApiUserService } from '@app/api/api/user.service';
import { UserDto } from '@app/api';
import { firstValueFrom } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly oidc = inject(OidcSecurityService);
  private readonly apiUser = inject(ApiUserService);

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
