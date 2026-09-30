import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    title: 'Sign in · Neural Damage',
    loadComponent: () => import('./features/auth/login').then((m) => m.Login),
  },
  {
    path: 'callback',
    title: 'Signing in · Neural Damage',
    loadComponent: () => import('./features/auth/callback').then((m) => m.Callback),
  },
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./features/shell/shell').then((m) => m.Shell),
    children: [
      {
        path: '',
        title: 'Neural Damage',
        loadComponent: () => import('./features/chat/home').then((m) => m.Home),
      },
      {
        path: 'chat/:chatId',
        title: 'Neural Damage',
        loadComponent: () => import('./features/chat/chat').then((m) => m.Chat),
      },
      {
        path: 'bots',
        title: 'Bots · Neural Damage',
        loadComponent: () => import('./features/bots/bots-page').then((m) => m.BotsPage),
      },
      {
        path: 'settings',
        title: 'Settings · Neural Damage',
        loadComponent: () => import('./features/settings/settings').then((m) => m.Settings),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
