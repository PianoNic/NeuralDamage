import { EnvironmentProviders, makeEnvironmentProviders } from '@angular/core';
import {
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
 * environment. The access token is only attached to this API's `/api/` requests.
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
                  scope: config.scope,
                  responseType: 'code',
                  silentRenew: false,
                  useRefreshToken: false,
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
  ]);
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

/** angular-auth-oidc-client keeps its whole state in this tab's sessionStorage. */
export function clearOidcSession(): void {
  try {
    sessionStorage.clear();
  } catch {
    // Storage blocked.
  }
}
