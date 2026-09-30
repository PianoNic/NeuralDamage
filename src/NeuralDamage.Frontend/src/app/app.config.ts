import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding, withViewTransitions } from '@angular/router';
import { authInterceptor } from 'angular-auth-oidc-client';
import { environment } from '../environments/environment';
import { Configuration } from './api/configuration';
import { routes } from './app.routes';
import { provideOidc } from './core/auth/oidc';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(
      routes,
      withComponentInputBinding(),
      withViewTransitions({ skipInitialTransition: true }),
    ),
    provideHttpClient(withFetch(), withInterceptors([authInterceptor()])),
    // Development talks to the API on its own port (CORS); production serves both from one host.
    { provide: Configuration, useFactory: () => new Configuration({ basePath: environment.apiBaseUrl }) },
    provideOidc(),
  ],
};
