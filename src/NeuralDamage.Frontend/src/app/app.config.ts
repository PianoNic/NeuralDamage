import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  provideAuth,
  authInterceptor,
  StsConfigHttpLoader,
  StsConfigLoader,
  LogLevel,
} from 'angular-auth-oidc-client';
import { map } from 'rxjs';
import { Configuration } from './api';
import { environment } from '../environments/environment';
import { routes } from './app.routes';

const httpLoaderFactory = (httpClient: HttpClient) => {
  const config$ = httpClient
    .get<any>(`${environment.apiBaseUrl}/api/App/config`)
    .pipe(
      map((c) => ({
        authority: c.authority,
        redirectUrl: c.redirectUri,
        postLogoutRedirectUri: c.postLogoutRedirectUri,
        clientId: c.clientId,
        scope: c.scope,
        responseType: 'code',
        silentRenew: false,
        useRefreshToken: false,
        secureRoutes: [environment.apiBaseUrl],
        unauthorizedRoute: '/login',
        logLevel: environment.production ? LogLevel.None : LogLevel.Debug,
      })),
    );
  return new StsConfigHttpLoader(config$);
};

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(withInterceptors([authInterceptor()])),
    provideAuth({
      loader: {
        provide: StsConfigLoader,
        useFactory: httpLoaderFactory,
        deps: [HttpClient],
      },
    }),
    {
      provide: Configuration,
      useFactory: () => new Configuration({ basePath: environment.apiBaseUrl }),
    },
  ],
};
