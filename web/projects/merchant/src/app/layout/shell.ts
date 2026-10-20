import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

import { OrderDto } from '../core/api';
import { Auth } from '../core/auth';
import { CurrentVenue } from '../core/current-venue';
import { LiveOrders } from '../core/live-orders';
import { formatMoney } from '../core/money';
import { NAV_ITEMS } from './nav';

interface Toast {
  id: string;
  order: OrderDto;
}

/**
 * Signed-in layout: sidebar navigation (blueprint 4.4), venue and user in the top bar, and live
 * new-order alerts on every page (toast, chime, nav badge and tab title).
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

    this.live.placed.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(order => {
      const toast = { id: order.id, order };
      this.toasts.update(list => [toast, ...list].slice(0, 4));
      setTimeout(() => this.dismiss(toast.id), 15_000);
    });
  }

  protected dismiss(id: string): void {
    this.toasts.update(list => list.filter(t => t.id !== id));
  }

  protected money(order: OrderDto): string {
    return formatMoney(order.totalMinor, order.currency);
  }

  protected signOut(): void {
    this.live.stop();
    this.auth.logout();
  }
}
