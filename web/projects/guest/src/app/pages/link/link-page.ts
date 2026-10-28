import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Subscription, filter, switchMap, takeWhile, timer } from 'rxjs';

import { errorCode } from '../../core/guest-session';
import { I18n } from '../../core/i18n';
import { newId } from '../../core/ids';
import { LinkPayment, PaymentLink, PaymentLinksApi, linkEqualShare, pendingLinkPaymentKey } from '../../core/links';
import { formatMoney } from '../../core/menu';
import { CustomTip, METHOD_ORDER, PaymentMethod, isFinal, parseAmount, parseTip, percentOf, safeUrl } from '../../core/payments';
import { readJson, writeJson } from '../../core/storage';

type Mode = 'Full' | 'Equal' | 'Custom';
type TipChoice = { kind: 'none' } | { kind: 'preset'; basisPoints: number } | { kind: 'custom' };

/** How long to keep checking before suggesting the customer goes back to the payment page. */
const PATIENCE_MS = 60_000;

/**
 * Payment link checkout (blueprint 4.2 "Payment Link Checkout", mockup "Link expired or paid"):
 * venue branding, the link's identifier and reference, the amount inclusive of taxes and
 * charges, a tip and split options when the merchant enabled them, and the payment methods.
 * Expired, paid or deactivated links say so clearly. The provider sends the customer back here
 * with ?payment=…, and the page waits for the provider's confirmation.
 */
@Component({
  selector: 'df-link-page',
  templateUrl: './link-page.html'
})
export class LinkPage implements OnInit {
  private readonly api = inject(PaymentLinksApi);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly i18n = inject(I18n);

  /** Route parameter: the token at the end of the shared link. */
  readonly token = input.required<string>();
  /** Set when the provider sends the customer back. */
  readonly payment = input<string>();
  /** "1" when the customer pressed Cancel on the provider's page. */
  readonly cancelled = input<string>();

  protected readonly link = signal<PaymentLink | null>(null);
  protected readonly loadError = signal<string | null>(null);

  // Checkout
  protected readonly mode = signal<Mode>('Full');
  protected readonly people = signal(2);
  protected readonly shares = signal(1);
  protected readonly customText = signal('');
  protected readonly tipChoice = signal<TipChoice>({ kind: 'none' });
  protected readonly customTipText = signal('');
  protected readonly chosenMethod = signal<PaymentMethod | null>(null);
  protected readonly paying = signal(false);
  protected readonly payError = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  /** A payment this customer started and hasn't finished, e.g. they came back from the provider's page. */
  protected readonly ownPending = signal<LinkPayment | null>(null);

  // After the provider
  protected readonly result = signal<LinkPayment | null>(null);
  protected readonly resultMissing = signal(false);
  protected readonly slow = signal(false);
  protected readonly email = signal('');
  protected readonly sending = signal(false);
  protected readonly sentTo = signal<string | null>(null);
  protected readonly receiptError = signal<string | null>(null);

  private clientRequestId: string | null = null;
  private requestKey = '';
  private defaulted = false;
  private polling: Subscription | null = null;
  private slowTimer: ReturnType<typeof setTimeout> | undefined;

  protected readonly currency = computed(() => this.link()?.currency ?? 'AED');
  protected readonly methods = computed(() => METHOD_ORDER.filter(m => this.link()?.methods.includes(m)));
  protected readonly method = computed(() => this.chosenMethod() ?? this.methods()[0] ?? null);
  protected readonly custom = computed(() => parseAmount(this.customText(), this.currency()));

  /** What this customer pays towards the link, before tip, as the server will work it out. */
  protected readonly share = computed(() => {
    const link = this.link();
    if (!link) return 0;
    switch (this.mode()) {
      case 'Equal': return linkEqualShare(link, this.people(), this.shares());
      case 'Custom': {
        const custom = this.custom();
        return custom && 'minor' in custom ? custom.minor : 0;
      }
      default: return link.availableMinor;
    }
  });

  protected readonly customTip = computed<CustomTip>(() => parseTip(this.customTipText(), this.share(), this.currency(), this.link()?.tips.maxBasisPoints));
  protected readonly tipMinor = computed(() => {
    const choice = this.tipChoice();
    if (!this.link()?.tips.enabled) return 0;
    if (choice.kind === 'preset') return percentOf(this.share(), choice.basisPoints);
    if (choice.kind === 'custom') {
      const custom = this.customTip();
      return custom && 'minor' in custom ? custom.minor : 0;
    }
    return 0;
  });
  protected readonly total = computed(() => this.share() + this.tipMinor());

  /** Why the custom amount can't be paid, if it can't. */
  protected readonly customError = computed(() => {
    if (this.mode() !== 'Custom') return null;
    const t = this.i18n.t().link;
    const custom = this.custom();
    if (custom && 'error' in custom) return t.amountInvalid;
    if (custom && 'minor' in custom && custom.minor > (this.link()?.availableMinor ?? 0)) return t.amountTooHigh(this.money(this.link()!.availableMinor));
    return null;
  });

  protected readonly canPay = computed(() => {
    const link = this.link();
    const tip = this.customTip();
    return !!link && link.status === 'Active' && this.share() > 0 && !this.customError() && !!this.method() && !this.paying()
      && !(this.tipChoice().kind === 'custom' && tip && 'error' in tip);
  });

  ngOnInit(): void {
    const paymentId = this.payment();
    if (paymentId) this.watch(paymentId, this.cancelled() === '1');
    else this.load();
    this.destroyRef.onDestroy(() => {
      this.polling?.unsubscribe();
      clearTimeout(this.slowTimer);
    });
  }

  protected money(minor: number): string {
    return formatMoney(minor, this.currency(), this.i18n.lang());
  }

  protected percent(basisPoints: number): string {
    return `${+(basisPoints / 100).toFixed(2)}%`;
  }

  protected presetAmount(basisPoints: number): number {
    return percentOf(this.share(), basisPoints);
  }

  /** One equal share of the link's amount (rounded up, as the server works it out). */
  protected eachShare(): number {
    return Math.ceil((this.link()?.amountMinor ?? 0) / this.people());
  }

  protected isPreset(basisPoints: number): boolean {
    const choice = this.tipChoice();
    return choice.kind === 'preset' && choice.basisPoints === basisPoints;
  }

  protected initials(name: string): string {
    return name.split(/\s+/).filter(Boolean).slice(0, 2).map(w => w[0]!.toUpperCase()).join('');
  }

  protected when(iso: string): string {
    return new Intl.DateTimeFormat(this.i18n.locale(), { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit', hour12: false })
      .format(new Date(iso));
  }

  protected methodText(payment: LinkPayment): string {
    const name = this.i18n.t().methodNames[payment.method] ?? payment.method;
    return payment.cardLast4 ? `${payment.cardBrand ?? name} •••• ${payment.cardLast4}` : name;
  }

  protected setPeople(people: number): void {
    const value = Math.min(50, Math.max(2, people));
    this.people.set(value);
    if (this.shares() > value) this.shares.set(value);
  }

  protected setShares(shares: number): void {
    this.shares.set(Math.min(this.people(), Math.max(1, shares)));
  }

  protected pay(): void {
    const link = this.link();
    const method = this.method();
    if (!link || !method || !this.canPay()) return;

    const mode = this.mode();
    const request = {
      method,
      mode,
      people: mode === 'Equal' ? this.people() : null,
      shares: mode === 'Equal' ? this.shares() : null,
      amountMinor: mode === 'Custom' ? this.share() : null,
      tipMinor: this.tipMinor(),
      expectedAmountMinor: this.share()
    };
    // The same id is reused while nothing changed, so a retry can't start a second payment.
    const key = JSON.stringify(request);
    if (key !== this.requestKey || !this.clientRequestId) {
      this.clientRequestId = newId();
      this.requestKey = key;
    }

    this.paying.set(true);
    this.payError.set(null);
    this.notice.set(null);
    this.api.pay(this.token(), { clientRequestId: this.clientRequestId, acceptTerms: true, ...request }).subscribe({
      next: payment => {
        writeJson('session', pendingLinkPaymentKey(link.code), payment.id);
        const url = safeUrl(payment.checkoutUrl);
        if (url) window.location.assign(url);
        else this.watch(payment.id, false);
      },
      error: (error: unknown) => {
        this.paying.set(false);
        const code = errorCode(error);
        const details = error instanceof HttpErrorResponse ? (error.error?.details as PaymentLink | undefined) : undefined;
        if (details) this.setLink(details);

        const t = this.i18n.t();
        if (code === 'amount_changed') this.payError.set(t.link.amountChanged);
        else if (code === 'payment_in_progress') this.payError.set(t.link.someonePayingRest);
        else if (code === 'amount_too_high') this.payError.set(t.link.amountTooHigh(this.money(details?.availableMinor ?? 0)));
        else if (code === 'tip_too_high') this.payError.set(t.customTipTooHigh);
        else if (code === 'link_paid' || code === 'link_expired' || code === 'link_cancelled') this.load();
        else this.payError.set(t.payFailed);
      }
    });
  }

  /** Back to the provider's page for a payment this customer started. */
  protected continuePending(): void {
    const url = safeUrl(this.ownPending()?.checkoutUrl);
    if (url) window.location.assign(url);
  }

  protected cancelPending(): void {
    const pending = this.ownPending();
    if (!pending) return;
    this.api.cancel(this.token(), pending.id).subscribe({ next: () => this.load(), error: () => this.load() });
  }

  /** From the result screen back to the link, e.g. to try again or pay another share. */
  protected backToPayment(): void {
    this.polling?.unsubscribe();
    clearTimeout(this.slowTimer);
    this.result.set(null);
    this.resultMissing.set(false);
    this.slow.set(false);
    this.paying.set(false);
    this.router.navigate(['/l', this.token()], { replaceUrl: true });
    this.load();
  }

  protected returnToProvider(): void {
    const url = safeUrl(this.result()?.checkoutUrl);
    if (url) window.location.assign(url);
  }

  protected cancelResult(): void {
    const payment = this.result();
    if (payment) this.api.cancel(this.token(), payment.id).subscribe(p => this.result.set(p));
  }

  protected sendReceipt(): void {
    const payment = this.result();
    const email = this.email().trim();
    if (!payment || !email || this.sending()) return;

    this.sending.set(true);
    this.receiptError.set(null);
    this.api.sendReceipt(this.token(), payment.id, email).subscribe({
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

  protected load(): void {
    this.api.get(this.token()).subscribe({
      next: link => {
        this.loadError.set(null);
        this.setLink(link);
      },
      error: (error: unknown) => this.loadError.set(errorCode(error))
    });
  }

  private setLink(link: PaymentLink): void {
    this.link.set(link);
    this.i18n.useDefault(link.venue.defaultLocale);

    if (!this.defaulted) {
      this.defaulted = true;
      // The venue's "Most common" tip starts selected (P1-PAY-11); the customer can pick "Not now".
      if (link.tips.enabled && link.tips.mostCommonBasisPoints !== null)
        this.tipChoice.set({ kind: 'preset', basisPoints: link.tips.mostCommonBasisPoints });
    }
    if (!link.allowSplit) this.mode.set('Full');

    const pendingId = readJson<string>('session', pendingLinkPaymentKey(link.code));
    if (link.paymentInProgress && pendingId) {
      this.api.payment(this.token(), pendingId).subscribe({
        next: payment => this.ownPending.set(payment.status === 'RequiresAction' && payment.checkoutUrl ? payment : null),
        error: () => this.ownPending.set(null)
      });
    } else {
      this.ownPending.set(null);
    }
  }

  /** Shows a payment's outcome, checking every two seconds until the provider has confirmed it. */
  private watch(paymentId: string, cancelled: boolean): void {
    this.load();
    const first = cancelled ? this.api.cancel(this.token(), paymentId) : this.api.payment(this.token(), paymentId);
    first.subscribe({
      next: payment => this.show(payment),
      error: (error: unknown) => this.resultMissing.set(errorCode(error) !== 'network')
    });

    this.polling?.unsubscribe();
    this.polling = timer(2_000, 2_000).pipe(
      filter(() => !this.result() || !isFinal(this.result()!.status)),
      switchMap(() => this.api.payment(this.token(), paymentId)),
      takeWhile(payment => !isFinal(payment.status), true)
    ).subscribe({ next: payment => this.show(payment), error: () => undefined });

    clearTimeout(this.slowTimer);
    this.slowTimer = setTimeout(() => this.slow.set(true), PATIENCE_MS);
  }

  private show(payment: LinkPayment): void {
    const link = this.link();
    if (payment.status === 'Cancelled') {
      // Nothing was charged: straight back to the link, with a note.
      if (link) writeJson('session', pendingLinkPaymentKey(link.code), null);
      this.backToPayment();
      this.notice.set(this.i18n.t().link.notCompleted);
      return;
    }
    this.result.set(payment);
    if (isFinal(payment.status)) {
      if (link) writeJson('session', pendingLinkPaymentKey(link.code), null);
      if (payment.status === 'Succeeded') this.load();
    }
  }
}
