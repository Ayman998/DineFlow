/** Merchant navigation from the blueprint (section 4.4), with the sprint that builds each area. Titles are in I18n's "nav". */
export const NAV_ITEMS = [
  { path: 'overview', sprint: 7 },
  { path: 'orders', sprint: 3 },
  { path: 'tables', sprint: 1 },
  { path: 'transactions', sprint: 7 },
  { path: 'menu', sprint: 2 },
  { path: 'item-categories', sprint: 12 },
  { path: 'items', sprint: 12 },
  { path: 'promotions', sprint: 4 },
  { path: 'payment-links', sprint: 8 },
  { path: 'reviews', sprint: 6 },
  { path: 'reports', sprint: 9 },
  { path: 'settings', sprint: 1 }
] as const;
