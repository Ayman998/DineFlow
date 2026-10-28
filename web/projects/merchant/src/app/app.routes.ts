import { Route, Routes } from '@angular/router';

import { authGuard } from './core/auth';
import { NAV_ITEMS } from './layout/nav';
import { Shell } from './layout/shell';

type LoadComponent = NonNullable<Route['loadComponent']>;

/** Areas built so far; the rest show a placeholder until their sprint. */
const PAGES: Partial<Record<string, LoadComponent>> = {
  orders: () => import('./pages/orders/orders').then(m => m.Orders),
  tables: () => import('./pages/tables/tables').then(m => m.Tables),
  menu: () => import('./pages/menu/menu-admin').then(m => m.MenuAdmin),
  promotions: () => import('./pages/promotions/promotions').then(m => m.Promotions),
  reviews: () => import('./pages/reviews/reviews').then(m => m.Reviews),
  overview: () => import('./pages/overview/overview').then(m => m.Overview),
  transactions: () => import('./pages/transactions/transactions').then(m => m.Transactions),
  'payment-links': () => import('./pages/payment-links/payment-links').then(m => m.PaymentLinks),
  reports: () => import('./pages/reports/reports').then(m => m.Reports),
  settings: () => import('./pages/settings/settings').then(m => m.Settings)
};

const placeholder: LoadComponent = () => import('./pages/placeholder/placeholder').then(m => m.Placeholder);

export const routes: Routes = [
  {
    path: 'login',
    data: { titleKey: 'signIn' },
    loadComponent: () => import('./pages/login/login').then(m => m.Login)
  },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'overview' },
      ...NAV_ITEMS.map(item => ({
        path: item.path,
        data: { titleKey: item.path, sprint: item.sprint },
        loadComponent: PAGES[item.path] ?? placeholder
      }))
    ]
  },
  { path: '**', redirectTo: '' }
];
