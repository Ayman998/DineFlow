import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

import { OrderDto, PaymentNotice } from '../core/api';
import { Auth } from '../core/auth';
import { CurrentVenue } from '../core/current-venue';
import { LiveOrders } from '../core/live-orders';
import { formatMoney } from '../core/money';
import { NAV_ITEMS } from './nav';

type Toast =
  | { id: string; kind: 'order'; order: OrderDto }
  | { id: string; kind: 'payment'; payment: PaymentNotice };

/**
 * Signed-in layout: sidebar navigation (blueprint 4.4), venue and user in the top bar, and live
 * alerts on every page: new orders (toast, chime, nav badge and tab title) and payments.
 */
@Component({
  selector: 'df-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './shell.html'
})
export class Shell implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  protected readonly nav = NAV_ITEMS;
  protected readonly auth = inject(Auth);
  protected readonly currentVenue = inject(CurrentVenue);
  protected readonly live = inject(LiveOrders);
  protected readonly toasts = signal<Toast[]>([]);

  ngOnInit(): void {
    this.currentVenue.load().subscribe(() => this.live.start());

    this.live.placed.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(order =>
      this.show({ id: order.id, kind: 'order', order }));
    this.live.paid.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(payment =>
      this.show({ id: payment.paymentId, kind: 'payment', payment }));
  }

  private show(toast: Toast): void {
    this.toasts.update(list => [toast, ...list.filter(t => t.id !== toast.id)].slice(0, 4));
    setTimeout(() => this.dismiss(toast.id), 15_000);
  }

  protected dismiss(id: string): void {
    this.toasts.update(list => list.filter(t => t.id !== id));
  }

  /** What a split payment covered, e.g. "1 of 4 shares"; null for the whole bill. */
  protected splitText(p: PaymentNotice): string | null {
    switch (p.mode) {
      case 'Equal': return `${p.shares} of ${p.splitPeople} shares`;
      case 'Items': return p.itemCount === 1 ? '1 item' : `${p.itemCount} items`;
      case 'Custom': return 'Custom amount';
      default: return null;
    }
  }

  protected money(minor: number, currency: string): string {
    return formatMoney(minor, currency);
  }

  protected signOut(): void {
    this.live.stop();
    this.auth.logout();
  }
}
