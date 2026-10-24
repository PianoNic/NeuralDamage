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
        pathMatch: 'full',
        title: 'Neural Damage',
        loadComponent: () => import('./features/feed/feed-page').then((m) => m.FeedPage),
      },
      {
        path: 'chats',
        loadComponent: () => import('./features/chat/chats-layout').then((m) => m.ChatsLayout),
        children: [
          {
            path: '',
            title: 'Chats · Neural Damage',
            loadComponent: () => import('./features/chat/home').then((m) => m.Home),
          },
          {
            path: ':chatId',
            title: 'Neural Damage',
            loadComponent: () => import('./features/chat/chat').then((m) => m.Chat),
          },
        ],
      },
      { path: 'chat/:chatId', redirectTo: 'chats/:chatId' },
      {
        path: 'bots',
        title: 'Bots · Neural Damage',
        loadComponent: () => import('./features/bots/bots-page').then((m) => m.BotsPage),
      },
      {
        path: 'bots/:botId',
        title: 'Bot · Neural Damage',
        loadComponent: () => import('./features/bots/bot-profile').then((m) => m.BotProfile),
      },
      { path: 'feed', pathMatch: 'full', redirectTo: '' },
      {
        path: 'feed/new',
        title: 'New post · Neural Damage',
        loadComponent: () => import('./features/feed/new-post').then((m) => m.NewPostPage),
      },
      {
        path: 'activity',
        title: 'Activity · Neural Damage',
        loadComponent: () => import('./features/feed/activity-page').then((m) => m.ActivityPage),
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
        path: 'me',
        title: 'You · Neural Damage',
        loadComponent: () => import('./features/settings/me-page').then((m) => m.MePage),
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
