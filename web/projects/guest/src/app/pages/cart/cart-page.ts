import { Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';

import { Cart, CartLine, MAX_QUANTITY } from '../../core/cart';
import { GuestSession } from '../../core/guest-session';
import { I18n } from '../../core/i18n';
import { formatMoney } from '../../core/menu';

/**
 * The order so far. Sprint 2 lets guests build and adjust it; sending it to the kitchen and
 * server-side pricing arrive in Sprint 3 (mockup "Cart / order review").
 */
@Component({
  selector: 'df-cart-page',
  imports: [RouterLink],
  template: `
    @let t = i18n.t();
    <header class="flex items-center gap-3 px-5 pt-4 pb-3">
      <a routerLink="/menu" class="chip px-3" [attr.aria-label]="t.backToMenu">
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" class="rtl:rotate-180" aria-hidden="true"><path d="M15 18l-6-6 6-6" /></svg>
      </a>
      <h1 class="text-xl font-extrabold">{{ t.yourOrder }}</h1>
    </header>

    <main class="px-5 pb-10">
      @if (!cart.lines().length) {
        <p class="py-12 text-center text-ink-muted">{{ t.orderEmpty }}</p>
        <a routerLink="/menu" class="mx-auto block w-fit rounded-[14px] bg-brand px-6 py-3 font-bold text-white">{{ t.backToMenu }}</a>
      } @else {
        @for (line of cart.lines(); track line.key) {
          <article class="flex gap-3 border-b border-line py-4">
            <div class="min-w-0 flex-1">
              <h2 class="font-bold">{{ i18n.localize(line.name, line.nameAr) }}</h2>
              @if (line.options.length) {
                <p class="text-[13px] text-ink-muted">{{ optionText(line) }}</p>
              }
              @if (line.note) {
                <p class="text-[13px] text-ink-muted italic">“{{ line.note }}”</p>
              }
              <div class="mt-2 flex items-center gap-2">
                <button type="button" class="qty-btn size-9" [attr.aria-label]="t.decreaseQuantity" (click)="cart.setQuantity(line.key, line.quantity - 1)">−</button>
                <span class="min-w-4 text-center font-bold">{{ line.quantity }}</span>
                <button type="button" class="qty-btn size-9" [attr.aria-label]="t.increaseQuantity" [disabled]="line.quantity >= max"
                        (click)="cart.setQuantity(line.key, line.quantity + 1)">+</button>
                <button type="button" class="ms-2 text-sm font-semibold text-red-700" (click)="cart.setQuantity(line.key, 0)">{{ t.remove }}</button>
              </div>
            </div>
            <span class="font-extrabold">{{ money(line.unitPriceMinor * line.quantity) }}</span>
          </article>
        }

        <div class="flex items-center justify-between py-4 text-lg font-extrabold">
          <span>{{ t.total }}</span>
          <span>{{ money(cart.totalMinor()) }}</span>
        </div>
        <p class="rounded-xl bg-surface px-4 py-3 text-sm text-ink-muted" role="note">{{ t.orderingSoon }}</p>
      }
    </main>
  `
})
export class CartPage {
  protected readonly cart = inject(Cart);
  protected readonly i18n = inject(I18n);
  private readonly session = inject(GuestSession);

  protected readonly max = MAX_QUANTITY;
  private readonly currency = computed(() => this.session.current()?.context.venue.currency ?? 'AED');

  protected money(minor: number): string {
    return formatMoney(minor, this.currency(), this.i18n.lang());
  }

  protected optionText(line: CartLine): string {
    return line.options.map(o => this.i18n.localize(o.name, o.nameAr)).join(', ');
  }
}
