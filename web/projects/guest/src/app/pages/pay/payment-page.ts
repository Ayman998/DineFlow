import { Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { Observable, filter, switchMap, takeWhile, timer } from 'rxjs';

import { GuestSession, errorCode } from '../../core/guest-session';
import { I18n } from '../../core/i18n';
import { formatMoney } from '../../core/menu';
import { OrderUpdates, OrdersApi } from '../../core/orders';
import { PENDING_PAYMENT_KEY, Payment, PaymentsApi, isFinal, safeUrl } from '../../core/payments';
import { readJson, writeJson } from '../../core/storage';
import { ReviewResult } from '../../core/reviews';
import { OrderTracker } from '../../shared/order-tracker';
import { ReviewFlow } from './review-flow';

/** How long to keep checking before suggesting the guest goes back to the payment page. */
const PATIENCE_MS = 60_000;

/**
 * Where the provider sends the guest back to (blueprint 4.2 step 9, mockup "Payment
 * confirmation"). The page waits for the provider's confirmation, then shows what was paid,
 * the bill's status, an offer for the next visit and the e-mail receipt (P1-REC-01/02).
 */
@Component({
  selector: 'df-payment-page',
  imports: [RouterLink, OrderTracker, ReviewFlow],
  templateUrl: './payment-page.html'
})
export class PaymentPage implements OnInit {
  private readonly api = inject(PaymentsApi);
  private readonly orders = inject(OrdersApi);
  private readonly updates = inject(OrderUpdates);
  private readonly session = inject(GuestSession);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly i18n = inject(I18n);

  /** Route parameter. */
  readonly id = input.required<string>();
  /** "1" when the guest pressed Cancel on the provider's page. */
  readonly cancelled = input<string>();

  protected readonly payment = signal<Payment | null>(null);
  protected readonly notFound = signal(false);
  protected readonly slow = signal(false);
  protected readonly stage = signal(2);
  protected readonly copied = signal(false);

  protected readonly email = signal('');
  protected readonly sending = signal(false);
  protected readonly sentTo = signal<string | null>(null);
  protected readonly receiptError = signal<string | null>(null);

  /** The rating sheet (P1-REV-04) opens by itself once, after a successful payment. */
  protected readonly reviewOpen = signal(false);
  protected readonly reviewed = signal<ReviewResult | null>(null);

  protected readonly context = computed(() => this.session.current()?.context ?? null);

  ngOnInit(): void {
    const first: Observable<Payment> = this.cancelled() === '1' ? this.api.cancel(this.id()) : this.api.get(this.id());
    first.subscribe({
      next: payment => this.show(payment),
      error: (error: unknown) => this.notFound.set(errorCode(error) !== 'network')
    });

    // The provider's confirmation can take a moment: check every two seconds until it's final.
    timer(2_000, 2_000).pipe(
      filter(() => !this.payment() || !isFinal(this.payment()!.status)),
      switchMap(() => this.api.get(this.id())),
      takeWhile(payment => !isFinal(payment.status), true),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({ next: payment => this.show(payment), error: () => undefined });

    const slowTimer = setTimeout(() => this.slow.set(true), PATIENCE_MS);
    this.destroyRef.onDestroy(() => clearTimeout(slowTimer));

    // Someone else at the table may pay the rest; keep the remaining balance current.
    this.updates.start();
    this.updates.billChanged.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      if (this.payment()?.status === 'Succeeded') this.api.get(this.id()).subscribe(p => this.show(p));
    });
  }

  protected money(minor: number): string {
    return formatMoney(minor, this.payment()?.currency ?? 'AED', this.i18n.lang());
  }

  protected when(payment: Payment): string {
    const date = new Date(payment.succeededAt ?? payment.createdAt);
    // "20 Oct 2026 · 16:42", as on the mockup.
    const locale = this.i18n.lang() === 'ar' ? 'ar-u-nu-latn' : 'en-GB';
    const day = new Intl.DateTimeFormat(locale, { day: '2-digit', month: 'short', year: 'numeric' }).format(date);
    const time = new Intl.DateTimeFormat(locale, { hour: '2-digit', minute: '2-digit', hour12: false }).format(date);
    return `${day} · ${time}`;
  }

  /** What this payment covered when the bill was split, e.g. "Your share (1 of 4 people)". */
  protected shareLabel(payment: Payment): string | null {
    const t = this.i18n.t();
    switch (payment.mode) {
      case 'Equal': return t.yourShareOf(payment.shares ?? 1, payment.splitPeople ?? 1);
      case 'Items': return t.yourItems(payment.itemCount);
      case 'Custom': return t.customAmount;
      default: return null;
    }
  }

  protected methodText(payment: Payment): string {
    const name = this.i18n.t().methodNames[payment.method] ?? payment.method;
    return payment.cardLast4 ? `${payment.cardBrand ?? name} •••• ${payment.cardLast4}` : name;
  }

  protected returnToProvider(): void {
    const url = safeUrl(this.payment()?.checkoutUrl);
    if (url) window.location.assign(url);
  }

  protected cancel(): void {
    this.api.cancel(this.id()).subscribe(payment => this.show(payment));
  }

  protected copy(code: string): void {
    navigator.clipboard?.writeText(code).then(() => {
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 2_000);
    }).catch(() => undefined);
  }

  protected sendReceipt(): void {
    const email = this.email().trim();
    if (!email || this.sending()) return;

    this.sending.set(true);
    this.receiptError.set(null);
    this.api.sendReceipt(this.id(), email).subscribe({
      next: () => {
        this.sending.set(false);
        this.sentTo.set(email);
      },
      error: () => {
        this.sending.set(false);
        this.receiptError.set(this.i18n.t().receiptFailed);
      }
    });
  }

  protected rateLater(): void {
    this.reviewOpen.set(true);
  }

  protected onReviewDone(result: ReviewResult | null): void {
    this.reviewOpen.set(false);
    writeJson('session', reviewAskedKey(this.id()), true);
    if (result) this.reviewed.set(result);
  }

  private show(payment: Payment): void {
    const wasSucceeded = this.payment()?.status === 'Succeeded';
    this.payment.set(payment);
    if (payment.status === 'Succeeded' && !wasSucceeded && payment.canReview && !readJson<boolean>('session', reviewAskedKey(payment.id)))
      setTimeout(() => this.reviewOpen.set(true), 600);
    if (isFinal(payment.status)) writeJson('session', PENDING_PAYMENT_KEY, null);

    // After paying, the tracker shows Checkout done; Delivered once every order has been served.
    const sessionId = this.context()?.sessionId;
    if (payment.status === 'Succeeded' && !wasSucceeded && sessionId) {
      this.orders.list(sessionId).subscribe(orders => {
        const live = orders.filter(o => o.status !== 'Rejected');
        this.stage.set(live.length && live.every(o => o.status === 'Completed') ? 3 : 2);
      });
    }
  }
}

function reviewAskedKey(paymentId: string): string {
  return `df.reviewAsked.${paymentId}`;
}
