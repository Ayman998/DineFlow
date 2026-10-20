import { Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';

import { GuestSession } from '../../core/guest-session';
import { I18n } from '../../core/i18n';
import { formatMoney } from '../../core/menu';
import { Order, OrderUpdates, OrdersApi, trackerStage } from '../../core/orders';
import { OrderTracker } from '../../shared/order-tracker';

/**
 * The table's orders with live progress (P1-ORD-04/05, blueprint 4.2 step 9): each round shows
 * the Selection → Checkout → Preparing → Delivered tracker, updated as staff move it along.
 */
@Component({
  selector: 'df-orders-page',
  imports: [RouterLink, OrderTracker],
  templateUrl: './orders-page.html'
})
export class OrdersPage implements OnInit {
  private readonly api = inject(OrdersApi);
  private readonly session = inject(GuestSession);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly updates = inject(OrderUpdates);
  protected readonly i18n = inject(I18n);

  /** From the query string after placing an order, e.g. ?placed=1004. */
  readonly placed = input<string>();

  protected readonly context = computed(() => this.session.current()?.context ?? null);
  protected readonly orders = signal<Order[] | null>(null);
  protected readonly stage = trackerStage;

  ngOnInit(): void {
    this.load();
    this.updates.start();
    this.updates.changed.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(order =>
      this.orders.update(list => {
        const current = list ?? [];
        return current.some(o => o.id === order.id)
          ? current.map(o => (o.id === order.id ? order : o))
          : [order, ...current];
      }));
    this.updates.reconnected.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.load());
  }

  protected money(order: Order, minor: number): string {
    return formatMoney(minor, order.currency, this.i18n.lang());
  }

  protected time(order: Order): string {
    return new Intl.DateTimeFormat(`${this.i18n.lang()}-u-nu-latn`, { hour: '2-digit', minute: '2-digit' }).format(new Date(order.createdAt));
  }

  protected options(line: Order['lines'][number]): string {
    return line.modifiers.map(m => this.i18n.localize(m.name, m.nameAr)).join(' · ');
  }

  private load(): void {
    const sessionId = this.context()?.sessionId;
    if (!sessionId) return;
    this.api.list(sessionId).subscribe({ next: orders => this.orders.set(orders), error: () => this.orders.set([]) });
  }
}
