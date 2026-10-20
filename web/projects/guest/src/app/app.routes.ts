import { Routes } from '@angular/router';

import { sessionGuard } from './core/session.guard';

export const routes: Routes = [
  { path: '', loadComponent: () => import('./pages/home/home').then(m => m.Home) },
  // Printed QR codes point here: /q/{opaque token}
  { path: 'q/:token', loadComponent: () => import('./pages/landing/landing').then(m => m.Landing) },
  {
    path: 'menu',
    canActivate: [sessionGuard],
    loadComponent: () => import('./pages/menu/menu').then(m => m.MenuPage)
  },
  {
    path: 'cart',
    canActivate: [sessionGuard],
    loadComponent: () => import('./pages/cart/cart-page').then(m => m.CartPage)
  },
  {
    path: 'bill',
    data: { feature: 'bill' },
    loadComponent: () => import('./pages/coming-soon/coming-soon').then(m => m.ComingSoon)
  },
  { path: '**', redirectTo: '' }
];
