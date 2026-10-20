import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';

import { GuestSession, errorCode } from '../../core/guest-session';
import { I18n } from '../../core/i18n';
import { newId } from '../../core/ids';
import { formatMoney } from '../../core/menu';
import { OrderUpdates } from '../../core/orders';
import {
  Bill, CustomTip, METHOD_ORDER, PENDING_PAYMENT_KEY, Payment, PaymentMethod, PaymentsApi, PromoQuote, parseTip, percentOf, safeUrl
} from '../../core/payments';
import { readJson, writeJson } from '../../core/storage';

type TipChoice = { kind: 'none' } | { kind: 'preset'; basisPoints: number } | { kind: 'custom' };

/**
 * Checkout (blueprint 4.2 steps 6–8, mockup "Checkout"): the table's bill, a tip, a promo code
 * and the payment method. The server works out what's owed; paying sends the guest to the
 * provider's secure page, and only the provider's confirmation marks the bill paid.
 */
@Component({
  selector: 'df-bill-page',
  imports: [RouterLink],
  templateUrl: './bill-page.html'
})
export class BillPage implements OnInit {
  private readonly api = inject(PaymentsApi);
  private readonly session = inject(GuestSession);
  private readonly updates = inject(OrderUpdates);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly i18n = inject(I18n);

  protected readonly context = computed(() => this.session.current()?.context ?? null);
  protected readonly bill = signal<Bill | null>(null);
  protected readonly loadFailed = signal(false);
  protected readonly showDetails = signal(false);
  /** A payment this guest started and hasn't finished, e.g. they came back from the provider's page. */
  protected readonly ownPending = signal<Payment | null>(null);

  protected readonly tipChoice = signal<TipChoice>({ kind: 'none' });
  protected readonly customText = signal('');

  protected readonly promoOpen = signal(false);
  protected readonly promoText = signal('');
  protected readonly promo = signal<PromoQuote | null>(null);
  protected readonly promoError = signal<string | null>(null);
  protected readonly checkingPromo = signal(false);

  protected readonly chosenMethod = signal<PaymentMethod | null>(null);
  protected readonly paying = signal(false);
  protected readonly payError = signal<string | null>(null);

  private clientRequestId: string | null = null;
  private requestKey = '';
  private tipDefaulted = false;

  protected readonly currency = computed(() => this.bill()?.currency ?? this.context()?.venue.currency ?? 'AED');
  protected readonly methods = computed(() => METHOD_ORDER.filter(m => this.bill()?.methods.includes(m)));
  protected readonly method = computed(() => this.chosenMethod() ?? this.methods()[0] ?? null);
  protected readonly due = computed(() => this.bill()?.remainingMinor ?? 0);
  protected readonly discount = computed(() => Math.min(this.promo()?.discountMinor ?? 0, this.due()));
  protected readonly tipsEnabled = computed(() => !!this.bill()?.tips.enabled);

  protected readonly customTip = computed<CustomTip>(() => parseTip(this.customText(), this.due(), this.currency()));

  protected readonly tipMinor = computed(() => {
    const choice = this.tipChoice();
    if (!this.tipsEnabled()) return 0;
    if (choice.kind === 'preset') return percentOf(this.due(), choice.basisPoints);
    if (choice.kind === 'custom') {
      const custom = this.customTip();
      return custom && 'minor' in custom ? custom.minor : 0;
    }
    return 0;
  });

  protected readonly total = computed(() => this.due() - this.discount() + this.tipMinor());

  protected readonly canPay = computed(() => {
    const bill = this.bill();
    const custom = this.customTip();
    return !!bill && bill.status === 'Open' && this.due() > 0 && !bill.paymentInProgress && !!this.method()
      && !this.paying() && !(this.tipChoice().kind === 'custom' && custom && 'error' in custom);
  });

  ngOnInit(): void {
    this.load();
    this.updates.start();
    this.updates.billChanged.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      if (!this.paying()) this.load();
    });
    this.updates.reconnected.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.load());
  }

  protected money(minor: number): string {
    return formatMoney(minor, this.currency(), this.i18n.lang());
  }

  protected percent(basisPoints: number): string {
    return `${+(basisPoints / 100).toFixed(2)}%`;
  }

  protected presetAmount(basisPoints: number): number {
    return percentOf(this.due(), basisPoints);
  }

  protected isPreset(basisPoints: number): boolean {
    const choice = this.tipChoice();
    return choice.kind === 'preset' && choice.basisPoints === basisPoints;
  }

  protected options(line: Bill['orders'][number]['lines'][number]): string {
    return line.modifiers.map(m => this.i18n.localize(m.name, m.nameAr)).join(' · ');
  }

  protected applyPromo(): void {
    const sessionId = this.context()?.sessionId;
    const code = this.promoText().trim();
    if (!sessionId || !code || this.checkingPromo()) return;

    this.checkingPromo.set(true);
    this.promoError.set(null);
    this.api.validatePromo(sessionId, code).subscribe({
      next: quote => {
        this.promo.set(quote);
        this.promoOpen.set(false);
        this.checkingPromo.set(false);
      },
      error: (error: unknown) => {
        this.checkingPromo.set(false);
        this.promoError.set(this.promoMessage(errorCode(error)));
      }
    });
  }

  protected removePromo(): void {
    this.promo.set(null);
    this.promoText.set('');
    this.promoError.set(null);
  }

  protected pay(): void {
    const sessionId = this.context()?.sessionId;
    const bill = this.bill();
    const method = this.method();
    if (!sessionId || !bill || !method || !this.canPay()) return;

    // The same id is reused while nothing changed, so a retry can't start a second payment.
    const request = { method, tipMinor: this.tipMinor(), promoCode: this.promo()?.code ?? null, expectedRemainingMinor: bill.remainingMinor };
    const key = JSON.stringify(request);
    if (key !== this.requestKey || !this.clientRequestId) {
      this.clientRequestId = newId();
      this.requestKey = key;
    }

    this.paying.set(true);
    this.payError.set(null);
    this.api.create({ sessionId, clientRequestId: this.clientRequestId, acceptTerms: true, ...request }).subscribe({
      next: payment => {
        writeJson('session', PENDING_PAYMENT_KEY, payment.id);
        const url = safeUrl(payment.checkoutUrl);
        if (url) window.location.assign(url);
        else this.router.navigate(['/pay', payment.id]);
      },
      error: (error: unknown) => {
        this.paying.set(false);
        const code = errorCode(error);
        const details = error instanceof HttpErrorResponse ? (error.error?.details as Bill | undefined) : undefined;
        if (details) this.setBill(details);

        const t = this.i18n.t();
        if (code === 'bill_changed') this.payError.set(t.billChangedNotice);
        else if (code === 'payment_in_progress') this.payError.set(t.someonePaying);
        else if (code === 'nothing_to_pay') this.load();
        else if (code.startsWith('promo_')) {
          this.promo.set(null);
          this.promoError.set(this.promoMessage(code));
        } else this.payError.set(t.payFailed);
      }
    });
  }

  /** Back to the provider's page for a payment this guest started. */
  protected continuePending(): void {
    const url = safeUrl(this.ownPending()?.checkoutUrl);
    if (url) window.location.assign(url);
  }

  protected cancelPending(): void {
    const pending = this.ownPending();
    if (!pending) return;
    this.api.cancel(pending.id).subscribe({ next: () => this.load(), error: () => this.load() });
  }

  protected load(): void {
    const sessionId = this.context()?.sessionId;
    if (!sessionId) return;
    this.api.bill(sessionId).subscribe({
      next: bill => {
        this.loadFailed.set(false);
        this.setBill(bill);
      },
      error: () => this.loadFailed.set(!this.bill())
    });
  }

  private setBill(bill: Bill): void {
    const previousDue = this.bill()?.remainingMinor;
    this.bill.set(bill);

    // The venue's "Most common" tip starts selected (P1-PAY-11); the guest can pick "Not now".
    if (!this.tipDefaulted && bill.tips.enabled && bill.tips.mostCommonBasisPoints !== null) {
      this.tipChoice.set({ kind: 'preset', basisPoints: bill.tips.mostCommonBasisPoints });
    }
    this.tipDefaulted = true;

    // A promotion's discount depends on the amount; check it again when the bill changes.
    const promo = this.promo();
    if (promo && previousDue !== undefined && previousDue !== bill.remainingMinor) {
      this.promoText.set(promo.code);
      this.promo.set(null);
      if (bill.status === 'Open' && bill.remainingMinor > 0) this.applyPromo();
    }

    const pendingId = readJson<string>('session', PENDING_PAYMENT_KEY);
    if (bill.paymentInProgress && pendingId) {
      this.api.get(pendingId).subscribe({
        next: payment => this.ownPending.set(payment.status === 'RequiresAction' && payment.checkoutUrl ? payment : null),
        error: () => this.ownPending.set(null)
      });
    } else {
      this.ownPending.set(null);
    }
  }

  private promoMessage(code: string): string {
    const t = this.i18n.t();
    return t.promoErrors[code] ?? t.promoErrors['promo_invalid']!;
  }
}
