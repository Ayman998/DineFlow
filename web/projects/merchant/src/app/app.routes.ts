import { Route, Routes } from '@angular/router';

import { authGuard } from './core/auth';
import { NAV_ITEMS } from './layout/nav';
import { Shell } from './layout/shell';

type LoadComponent = NonNullable<Route['loadComponent']>;

/** Areas built so far; the rest show a placeholder until their sprint. */
const PAGES: Partial<Record<string, LoadComponent>> = {
  tables: () => import('./pages/tables/tables').then(m => m.Tables),
  settings: () => import('./pages/settings/settings').then(m => m.Settings)
};

const placeholder: LoadComponent = () => import('./pages/placeholder/placeholder').then(m => m.Placeholder);

export const routes: Routes = [
  {
    path: 'login',
    title: 'Sign in · DineFlow',
    loadComponent: () => import('./pages/login/login').then(m => m.Login)
  },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'tables' },
      ...NAV_ITEMS.map(item => ({
        path: item.path,
        title: `${item.title} · DineFlow`,
        data: { title: item.title, sprint: item.sprint },
        loadComponent: PAGES[item.path] ?? placeholder
      }))
    ]
  },
  { path: '**', redirectTo: '' }
];
