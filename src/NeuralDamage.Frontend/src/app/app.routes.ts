import { Routes } from '@angular/router';
import { authGuard } from '@app/shared/auth/auth.guard';

export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./authentication/login/login').then((m) => m.LoginComponent) },
  { path: 'callback', loadComponent: () => import('./authentication/callback/callback').then((m) => m.CallbackComponent) },
  {
    path: '',
    loadComponent: () => import('./shared/layout/layout').then((m) => m.LayoutComponent),
    canActivate: [authGuard],
    children: [
      { path: '', loadComponent: () => import('./home/home').then((m) => m.HomeComponent) },
      { path: 'chat/:chatId', loadComponent: () => import('./chat/chat-view/chat-view').then((m) => m.ChatViewComponent) },
    ],
  },
];
