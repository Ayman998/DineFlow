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
    path: 'orders',
    canActivate: [sessionGuard],
    loadComponent: () => import('./pages/orders/orders-page').then(m => m.OrdersPage)
  },
  {
    path: 'bill',
    canActivate: [sessionGuard],
    loadComponent: () => import('./pages/bill/bill-page').then(m => m.BillPage)
  },
  // The payment provider sends the guest back here: /pay/{paymentId}[?cancelled=1]
  {
    path: 'pay/:id',
    canActivate: [sessionGuard],
    loadComponent: () => import('./pages/pay/payment-page').then(m => m.PaymentPage)
  },
  { path: '**', redirectTo: '' }
];
