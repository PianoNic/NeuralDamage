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
        path: 'feed',
        title: 'Feed · Neural Damage',
        loadComponent: () => import('./features/feed/feed-page').then((m) => m.FeedPage),
      },
      {
        path: 'feed/story',
        title: 'New story · Neural Damage',
        loadComponent: () => import('./features/stories/story-editor').then((m) => m.StoryEditor),
      },
      {
        path: 'feed/:postId',
        title: 'Post · Neural Damage',
        loadComponent: () => import('./features/feed/post-thread').then((m) => m.PostThread),
      },
      {
        path: 'stickers',
        title: 'Stickers · Neural Damage',
        loadComponent: () => import('./features/stickers/stickers-page').then((m) => m.StickersPage),
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
