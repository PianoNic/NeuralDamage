import { EnvironmentProviders, makeEnvironmentProviders } from '@angular/core';
import {
  AbstractSecurityStorage,
  DefaultLocalStorageService,
  LogLevel,
  provideAuth,
  StsConfigHttpLoader,
  StsConfigLoader,
} from 'angular-auth-oidc-client';
import { map } from 'rxjs';
import { environment } from '../../../environments/environment';
import { ApplicationConfigurationService } from '../../api/api/applicationConfiguration.service';
import { AppConfig } from '../app-config';

const AUTHORITY_KEY = 'neuraldamage.oidc.authority';

/**
 * The identity provider's settings come from `/api/app` at runtime, so the bundle carries no
 * environment. The access token is only attached to this API's `/api/` requests. The session lives
 * in localStorage and renews with refresh tokens, so closing the tab does not sign anyone out.
 */
export function provideOidc(): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideAuth({
      loader: {
        provide: StsConfigLoader,
        useFactory: (app: ApplicationConfigurationService, appConfig: AppConfig) =>
          new StsConfigHttpLoader(
            app.appConfiguration().pipe(
              map((config) => {
                appConfig.apply(config);
                forgetSessionOfOtherIssuer(config.authority);
                return {
                  authority: config.authority,
                  redirectUrl: config.redirectUri,
                  postLogoutRedirectUri: config.postLogoutRedirectUri,
                  clientId: config.clientId,
                  scope: withOfflineAccess(config.scope),
                  responseType: 'code',
                  silentRenew: true,
                  useRefreshToken: true,
                  // Refreshed ID tokens carry no nonce.
                  ignoreNonceAfterRefresh: true,
                  secureRoutes: [`${environment.apiBaseUrl}/api/`],
                  unauthorizedRoute: '/login',
                  logLevel: environment.production ? LogLevel.None : LogLevel.Warn,
                };
              }),
            ),
          ),
        deps: [ApplicationConfigurationService, AppConfig],
      },
    }),
    { provide: AbstractSecurityStorage, useClass: DefaultLocalStorageService },
  ]);
}

/** Refresh tokens need `offline_access`; it is added when the configured scope leaves it out. */
export function withOfflineAccess(scope: string): string {
  const scopes = scope.split(/\s+/).filter(Boolean);
  return (scopes.includes('offline_access') ? scopes : [...scopes, 'offline_access']).join(' ');
}

/**
 * A session stored for a different issuer fails validation on every load and would keep the user
 * out, so it is dropped when the server's configured authority changes.
 */
function forgetSessionOfOtherIssuer(authority: string): void {
  try {
    const previous = localStorage.getItem(AUTHORITY_KEY);
    if (previous && previous !== authority) clearOidcSession();
    localStorage.setItem(AUTHORITY_KEY, authority);
  } catch {
    // Storage blocked: nothing stored, nothing stale.
  }
}

/** Drops angular-auth-oidc-client's state: every stored key that isn't the app's own. */
export function clearOidcSession(): void {
  try {
    sessionStorage.clear();
    Object.keys(localStorage)
      .filter((key) => !key.startsWith('neuraldamage.') && !key.startsWith('nd.'))
      .forEach((key) => localStorage.removeItem(key));
  } catch {
    // Storage blocked.
  }
}
