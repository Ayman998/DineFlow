import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

import { OrderDto, PaymentLinkNotice, PaymentNotice, RefundNotice, ReviewNotice } from '../core/api';
import { Auth } from '../core/auth';
import { CurrentVenue } from '../core/current-venue';
import { Features } from '../core/features';
import { I18n } from '../core/i18n';
import { LiveOrders } from '../core/live-orders';
import { formatMoney } from '../core/money';
import { Logo } from '../shared/logo';
import { NAV_ITEMS } from './nav';

type Toast =
  | { id: string; kind: 'order'; order: OrderDto }
  | { id: string; kind: 'payment'; payment: PaymentNotice }
  | { id: string; kind: 'review'; review: ReviewNotice }
  | { id: string; kind: 'refund'; refund: RefundNotice }
  | { id: string; kind: 'link'; link: PaymentLinkNotice };

/** The areas staff open most, kept in the bottom tab bar on phones and tablets. */
const TAB_PATHS: readonly string[] = ['overview', 'orders', 'tables', 'transactions'];

/**
 * Signed-in layout: sidebar navigation (blueprint 4.4), venue and user in the top bar, and live
 * alerts on every page: new orders (toast, chime, nav badge and tab title) and payments.
 * Below lg the sidebar becomes a drawer and a bottom tab bar holds the busiest areas.
 */
@Component({
  selector: 'hz-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Logo],
  templateUrl: './shell.html',
  host: { '(document:keydown.escape)': 'menuOpen.set(false)' }
})
export class Shell implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly features = inject(Features);
  /** Areas whose feature is switched off in the API's configuration are left out. */
  protected readonly nav = computed(() => NAV_ITEMS.filter(item =>
    (item.path !== 'payment-links' || this.features.current().paymentLinks) && (item.path !== 'go-live' || this.auth.canManage())));
  protected readonly tabs = computed(() => this.nav().filter(item => TAB_PATHS.includes(item.path)));
  /** The navigation drawer on phones and tablets; the sidebar is always shown from lg up. */
  protected readonly menuOpen = signal(false);
  protected readonly auth = inject(Auth);
  protected readonly currentVenue = inject(CurrentVenue);
  protected readonly live = inject(LiveOrders);
  protected readonly toasts = signal<Toast[]>([]);
  protected readonly i18n = inject(I18n);
  protected readonly t = this.i18n.t;
  /** "Rana Haddad" → "RH", for the avatar in the top bar. */
  protected readonly initials = computed(() =>
    (this.auth.profile()?.displayName ?? '').split(/\s+/).filter(Boolean).slice(0, 2).map(word => word[0]).join('').toUpperCase());

  ngOnInit(): void {
    this.currentVenue.load().subscribe(() => this.live.start());
    this.features.load();

    this.live.placed.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(order =>
      this.show({ id: order.id, kind: 'order', order }));
    this.live.paid.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(payment =>
      this.show({ id: payment.paymentId, kind: 'payment', payment }));
    this.live.reviewed.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(review =>
      this.show({ id: review.reviewId, kind: 'review', review }));
    this.live.refunded.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(refund =>
      this.show({ id: refund.refundId, kind: 'refund', refund }));
    this.live.linkPaid.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(link =>
      this.show({ id: link.paymentId, kind: 'link', link }));
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
    const t = this.t();
    switch (p.mode) {
      case 'Equal': return t.sharesOf(p.shares ?? 0, p.splitPeople ?? 0);
      case 'Items': return t.items(p.itemCount);
      case 'Custom': return t.customAmount;
      default: return null;
    }
  }

  /** "Table 12" in the current language. */
  protected place(type: string, label: string): string {
    return `${this.t().locationTypes[type] ?? type} ${label}`;
  }

  protected money(minor: number, currency: string): string {
    return formatMoney(minor, currency);
  }

  protected signOut(): void {
    this.live.stop();
    this.auth.logout();
  }
}
