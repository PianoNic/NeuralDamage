import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/** Sends anyone without a session to the sign-in page. */
export const authGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (auth.isLoading()) await auth.checkAuth();

  return auth.isAuthenticated() ? true : router.createUrlTree(['/login']);
};
