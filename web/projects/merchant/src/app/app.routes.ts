import { Routes } from '@angular/router';

/** Merchant navigation from the blueprint (section 4.4), with the sprint that builds each area. */
export const NAV_ITEMS = [
  { path: 'overview', title: 'Overview', sprint: 7 },
  { path: 'orders', title: 'Orders', sprint: 3 },
  { path: 'tables', title: 'Tables', sprint: 1 },
  { path: 'transactions', title: 'Transactions', sprint: 7 },
  { path: 'menu', title: 'Menu', sprint: 2 },
  { path: 'payment-links', title: 'Payment Links', sprint: 8 },
  { path: 'reviews', title: 'Reviews', sprint: 6 },
  { path: 'reports', title: 'Reports', sprint: 9 },
  { path: 'settings', title: 'Settings', sprint: 1 }
] as const;

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'overview' },
  ...NAV_ITEMS.map(item => ({
    path: item.path,
    title: `${item.title} · DineFlow`,
    data: { title: item.title, sprint: item.sprint },
    loadComponent: () => import('./pages/placeholder/placeholder').then(m => m.Placeholder)
  })),
  { path: '**', redirectTo: 'overview' }
];
