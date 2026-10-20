import { Injectable, signal } from '@angular/core';

import { OrderCounts } from './api';

/**
 * How many orders are in each active state. Kept apart from LiveOrders so low-level pieces
 * such as the router's title strategy can read it without depending on auth and the router.
 */
@Injectable({ providedIn: 'root' })
export class OrderCountsStore {
  readonly counts = signal<OrderCounts>({ new: 0, accepted: 0, preparing: 0, ready: 0 });
}
