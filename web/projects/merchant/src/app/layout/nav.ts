/**
 * Merchant navigation from the blueprint (section 4.4), with the sprint that builds each area. Titles are in I18n's "nav";
 * icons are 24×24 stroke paths.
 */
export const NAV_ITEMS = [
  { path: 'overview', sprint: 7, icon: 'M4 4h6v8H4zM14 4h6v4h-6zM14 12h6v8h-6zM4 16h6v4H4z' },
  { path: 'orders', sprint: 3, icon: 'M9 3h6v3H9zM7 4.5H5.5A1.5 1.5 0 0 0 4 6v13.5A1.5 1.5 0 0 0 5.5 21h13a1.5 1.5 0 0 0 1.5-1.5V6a1.5 1.5 0 0 0-1.5-1.5H17M8 11h8M8 15h5' },
  { path: 'tables', sprint: 1, icon: 'M3 9h18M6 9v10M18 9v10M6 14h12M8 5h8' },
  { path: 'transactions', sprint: 7, icon: 'M7 7h13M16 3l4 4-4 4M17 17H4M8 13l-4 4 4 4' },
  { path: 'menu', sprint: 2, icon: 'M3 5.5A1.5 1.5 0 0 1 4.5 4H9a3 3 0 0 1 3 3v13a2.5 2.5 0 0 0-2.5-2.5h-5A1.5 1.5 0 0 1 3 16zM21 5.5A1.5 1.5 0 0 0 19.5 4H15a3 3 0 0 0-3 3v13a2.5 2.5 0 0 1 2.5-2.5h5A1.5 1.5 0 0 0 21 16z' },
  { path: 'item-categories', sprint: 12, icon: 'M3 4v8.5l9.5 9.5 9-9L12 3H4a1 1 0 0 0-1 1zM7.5 7.5h.01' },
  { path: 'items', sprint: 12, icon: 'M7 3v18M4 3v5a3 3 0 0 0 6 0V3M17 21V3c-2.5 1.5-3.5 4-3.5 8H17' },
  { path: 'promotions', sprint: 4, icon: 'M19 5L5 19M6.5 9a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5zM17.5 20a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5z' },
  { path: 'payment-links', sprint: 8, icon: 'M10 14a4.5 4.5 0 0 0 6.4 0l3-3a4.5 4.5 0 0 0-6.4-6.4l-1.2 1.2M14 10a4.5 4.5 0 0 0-6.4 0l-3 3a4.5 4.5 0 0 0 6.4 6.4l1.2-1.2' },
  { path: 'reviews', sprint: 6, icon: 'M12 3l2.8 5.7 6.2.9-4.5 4.4 1.1 6.2L12 17.3l-5.6 2.9 1.1-6.2L3 9.6l6.2-.9z' },
  { path: 'reports', sprint: 9, icon: 'M4 20h16M7 16v-5M12 16V6M17 16v-8' },
  { path: 'go-live', sprint: 12, icon: 'M5 21V4M5 4h12l-2.5 4 2.5 4H5' },
  { path: 'settings', sprint: 1, icon: 'M4 7h9M17 7h3M15 5v4M4 17h3M11 17h9M9 15v4' }
] as const;
