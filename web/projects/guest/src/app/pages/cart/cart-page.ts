import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import { Cart, CartLine, MAX_QUANTITY } from '../../core/cart';
import { GuestSession, errorCode } from '../../core/guest-session';
import { newId } from '../../core/ids';
import { I18n } from '../../core/i18n';
import { formatMoney } from '../../core/menu';
import { CartIssue, CartLineRequest, CartQuote, OrdersApi } from '../../core/orders';
import { OrderTracker } from '../../shared/order-tracker';

/**
 * Order review (blueprint 4.2 step 5, mockup "Cart / order review"). The server prices the
 * cart (P1-ORD-01) every time it changes, and placing the order is safe to retry (P1-ORD-02).
 */
@Component({
  selector: 'df-cart-page',
  imports: [RouterLink, OrderTracker],
  templateUrl: './cart-page.html'
})
export class CartPage {
  private readonly api = inject(OrdersApi);
  private readonly session = inject(GuestSession);
  private readonly router = inject(Router);
  protected readonly cart = inject(Cart);
  protected readonly i18n = inject(I18n);

  protected readonly max = MAX_QUANTITY;
  protected readonly context = computed(() => this.session.current()?.context ?? null);
  protected readonly quote = signal<CartQuote | null>(null);
  protected readonly quoting = signal(false);
  protected readonly placing = signal(false);
  protected readonly placeError = signal<string | null>(null);
  protected readonly priceChanged = signal(false);
  protected readonly note = signal('');

  /** The same id is reused while the cart is unchanged, so a retry can't create a second order. */
  private clientRequestId: string | null = null;
  private requestKey = '';
  private quoteTimer: ReturnType<typeof setTimeout> | undefined;

  protected readonly canPlace = computed(() =>
    !!this.quote()?.canOrder && !this.quoting() && !this.placing() && this.cart.count() > 0);

  constructor() {
    // Re-price on the server whenever the cart changes (a short pause batches quick taps).
    effect(() => {
      this.cart.lines();
      untracked(() => {
        clearTimeout(this.quoteTimer);
        this.quoteTimer = setTimeout(() => this.requote(), 250);
      });
    });
    inject(DestroyRef).onDestroy(() => clearTimeout(this.quoteTimer));
  }

  protected money(minor: number): string {
    return formatMoney(minor, this.quote()?.currency ?? this.context()?.venue.currency ?? 'AED', this.i18n.lang());
  }

  protected percent(basisPoints: number): string {
    return `${+(basisPoints / 100).toFixed(2)}%`;
  }

  protected optionText(line: CartLine): string {
    return line.options.map(o => this.i18n.localize(o.name, o.nameAr)).join(' · ');
  }

  /** The server's price for a line, falling back to the price shown when it was added. */
  protected lineTotal(index: number, line: CartLine): number {
    return this.quote()?.lines.find(l => l.index === index)?.lineTotalMinor ?? line.unitPriceMinor * line.quantity;
  }

  protected issuesFor(index: number): CartIssue[] {
    return this.quote()?.issues.filter(i => i.index === index) ?? [];
  }

  protected place(): void {
    const sessionId = this.context()?.sessionId;
    const quote = this.quote();
    if (!sessionId || !quote || !this.canPlace()) return;

    const lines = this.requestLines();
    const note = this.note().trim() || null;
    const key = JSON.stringify([lines, note]);
    if (key !== this.requestKey || !this.clientRequestId) {
      this.clientRequestId = newId();
      this.requestKey = key;
    }

    this.placing.set(true);
    this.placeError.set(null);
    this.api.place(sessionId, this.clientRequestId, lines, note, quote.totalMinor).subscribe({
      next: order => {
        this.cart.clear();
        this.router.navigate(['/orders'], { queryParams: { placed: order.number } });
      },
      error: (error: unknown) => {
        this.placing.set(false);
        const code = errorCode(error);
        const details = error instanceof HttpErrorResponse ? (error.error?.details as CartQuote | undefined) : undefined;
        if ((code === 'price_changed' || code === 'cart_invalid') && details) {
          // Nothing was ordered; show the server's new prices and let the guest confirm again.
          this.quote.set(details);
          this.priceChanged.set(code === 'price_changed');
        } else {
          this.placeError.set(this.i18n.t().placeFailed);
        }
      }
    });
  }

  private requote(): void {
    const sessionId = this.context()?.sessionId;
    if (!sessionId || !this.cart.lines().length) {
      this.quote.set(null);
      return;
    }

    this.quoting.set(true);
    this.api.quote(sessionId, this.requestLines()).subscribe({
      next: quote => {
        this.quote.set(quote);
        this.priceChanged.set(false);
        this.quoting.set(false);
      },
      error: () => this.quoting.set(false)
    });
  }

  private requestLines(): CartLineRequest[] {
    return this.cart.lines().map(l => ({
      itemId: l.itemId,
      quantity: l.quantity,
      optionIds: l.options.map(o => o.optionId),
      note: l.note || null
    }));
  }
}
