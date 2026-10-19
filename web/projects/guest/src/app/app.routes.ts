import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', loadComponent: () => import('./pages/home/home').then(m => m.Home) },
  // Sprint 1: { path: 'q/:token', ... } resolves a table QR token into a guest session.
  { path: '**', redirectTo: '' }
];
