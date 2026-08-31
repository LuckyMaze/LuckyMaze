import { Routes } from '@angular/router';
import { AppLayout } from './shared/layouts/app-layout/app-layout';
import { adminGuard } from './shared/auth/admin.guard';
import { authGuard } from './shared/auth/auth.guard';

export const routes: Routes = [
  { path: 'login', loadComponent: () => import('./auth/login/login').then(m => m.LoginComponent) },
  { path: 'register', loadComponent: () => import('./auth/register/register').then(m => m.RegisterComponent) },
  {
    path: '',
    component: AppLayout,
    canActivateChild: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'lobby' },
      { path: 'lobby', loadComponent: () => import('./lobby/lobby').then(m => m.LobbyComponent) },
      { path: 'leaderboard', loadComponent: () => import('./leaderboard/leaderboard').then(m => m.LeaderboardComponent) },
      { path: 'history', loadComponent: () => import('./history/history').then(m => m.HistoryComponent) },
      { path: 'admin', canActivate: [adminGuard], loadComponent: () => import('./admin/admin').then(m => m.AdminComponent) },
    ],
  },
  { path: '**', redirectTo: '' },
];
