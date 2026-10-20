import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', loadComponent: () => import('./pages/home/home').then(m => m.Home) },
  // Printed QR codes point here: /q/{opaque token}
  { path: 'q/:token', loadComponent: () => import('./pages/landing/landing').then(m => m.Landing) },
  {
    path: 'menu',
    data: { feature: 'menu' },
    loadComponent: () => import('./pages/coming-soon/coming-soon').then(m => m.ComingSoon)
  },
  {
    path: 'bill',
    data: { feature: 'bill' },
    loadComponent: () => import('./pages/coming-soon/coming-soon').then(m => m.ComingSoon)
  },
  { path: '**', redirectTo: '' }
];
