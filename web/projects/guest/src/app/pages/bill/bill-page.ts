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
  Bill, CustomTip, METHOD_ORDER, PENDING_PAYMENT_KEY, Payment, PaymentMethod, PaymentsApi, PromoQuote, Selection,
  parseTip, percentOf, principalOf, safeUrl
} from '../../core/payments';
import { readJson, writeJson } from '../../core/storage';
import { SplitSheet } from './split-sheet';

type TipChoice = { kind: 'none' } | { kind: 'preset'; basisPoints: number } | { kind: 'custom' };

/**
 * Checkout (blueprint 4.2 steps 6–8, mockup "Checkout"): the table's bill, how much of it this
 * guest pays (all of it, or a split: equal, by item or custom), a tip, a promo code and the
 * payment method. The server works out what's owed; paying sends the guest to the provider's
 * secure page, and only the provider's confirmation marks the bill paid.
 */
@Component({
  selector: 'df-bill-page',
  imports: [RouterLink, SplitSheet],
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

  /** What this guest is paying: the rest of the bill unless they split it. */
  protected readonly selection = signal<Selection>({ mode: 'Full' });
  protected readonly splitOpen = signal(false);
  protected readonly notice = signal<string | null>(null);

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
  private defaulted = false;
  private promoBase: number | null = null;

  protected readonly currency = computed(() => this.bill()?.currency ?? this.context()?.venue.currency ?? 'AED');
  protected readonly methods = computed(() => METHOD_ORDER.filter(m => this.bill()?.methods.includes(m)));
  protected readonly method = computed(() => this.chosenMethod() ?? this.methods()[0] ?? null);
  /** This guest's share of the bill, inclusive of taxes and charges, before discount and tip. */
  protected readonly share = computed(() => {
    const bill = this.bill();
    return bill ? principalOf(bill, this.selection()) : 0;
  });
  protected readonly discount = computed(() => Math.min(this.promo()?.discountMinor ?? 0, this.share()));
  protected readonly tipsEnabled = computed(() => !!this.bill()?.tips.enabled);
  protected readonly myItemCount = computed(() => this.bill()?.units.filter(u => u.mine && u.status === 'Selected').length ?? 0);

  protected readonly customTip = computed<CustomTip>(() => parseTip(this.customText(), this.share(), this.currency(), this.bill()?.tips.maxBasisPoints));

  protected readonly tipMinor = computed(() => {
    const choice = this.tipChoice();
    if (!this.tipsEnabled()) return 0;
    if (choice.kind === 'preset') return percentOf(this.share(), choice.basisPoints);
    if (choice.kind === 'custom') {
      const custom = this.customTip();
      return custom && 'minor' in custom ? custom.minor : 0;
    }
    return 0;
  });

  protected readonly total = computed(() => this.share() - this.discount() + this.tipMinor());

  protected readonly canPay = computed(() => {
    const bill = this.bill();
    const custom = this.customTip();
    return !!bill && bill.status === 'Open' && this.share() > 0 && !!this.method() && !this.paying() && !this.splitOpen()
      && !(this.tipChoice().kind === 'custom' && custom && 'error' in custom);
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
    return percentOf(this.share(), basisPoints);
  }

  protected isPreset(basisPoints: number): boolean {
    const choice = this.tipChoice();
    return choice.kind === 'preset' && choice.basisPoints === basisPoints;
  }

  protected options(line: Bill['orders'][number]['lines'][number]): string {
    return line.modifiers.map(m => this.i18n.localize(m.name, m.nameAr)).join(' · ');
  }

  /** One line describing the split, e.g. "Splitting equally · you pay for 1 of 4". */
  protected splitSummary(): string {
    const t = this.i18n.t();
    const selection = this.selection();
    const bill = this.bill();
    switch (selection.mode) {
      case 'Equal': return t.splitSummaryEqual(selection.shares, bill?.split.people ?? selection.shares);
      case 'Items': return t.splitSummaryItems(this.myItemCount());
      case 'Custom': return t.splitSummaryCustom;
      default: return '';
    }
  }

  protected openSplit(): void {
    this.notice.set(null);
    this.splitOpen.set(true);
  }

  protected payInFull(): void {
    const bill = this.bill();
    const sessionId = this.context()?.sessionId;
    this.selection.set({ mode: 'Full' });
    this.revalidatePromo();
    // Paying everything: let go of any items this guest was holding for others to see.
    if (bill && sessionId && bill.split.selectedMinor > 0)
      this.api.reserve(sessionId, []).subscribe({ next: r => this.setBill(r.bill), error: () => this.load() });
  }

  protected onSplitConfirmed(selection: Selection): void {
    this.selection.set(selection);
    this.splitOpen.set(false);
    this.revalidatePromo();
  }

  protected onSplitRemoved(): void {
    this.selection.set({ mode: 'Full' });
    this.splitOpen.set(false);
    this.revalidatePromo();
  }

  protected onSplitClosed(): void {
    this.splitOpen.set(false);
    // Closing without confirming: items picked in the sheet are only kept if paying by item.
    const bill = this.bill();
    const sessionId = this.context()?.sessionId;
    if (this.selection().mode !== 'Items' && bill && sessionId && bill.split.selectedMinor > 0)
      this.api.reserve(sessionId, []).subscribe({ next: r => this.setBill(r.bill), error: () => this.load() });
  }

  protected applyPromo(): void {
    const sessionId = this.context()?.sessionId;
    const code = this.promoText().trim();
    if (!sessionId || !code || this.checkingPromo()) return;

    this.checkingPromo.set(true);
    this.promoError.set(null);
    const base = this.share();
    this.api.validatePromo(sessionId, code, base > 0 ? base : null).subscribe({
      next: quote => {
        this.promo.set(quote);
        this.promoBase = base;
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
    this.promoBase = null;
    this.promoText.set('');
    this.promoError.set(null);
  }

  protected pay(): void {
    const sessionId = this.context()?.sessionId;
    const bill = this.bill();
    const method = this.method();
    if (!sessionId || !bill || !method || !this.canPay()) return;

    const selection = this.selection();
    // The same id is reused while nothing changed, so a retry can't start a second payment.
    const request = {
      method,
      mode: selection.mode,
      shares: selection.mode === 'Equal' ? selection.shares : null,
      amountMinor: selection.mode === 'Custom' ? this.share() : null,
      tipMinor: this.tipMinor(),
      promoCode: this.promo()?.code ?? null,
      expectedAmountMinor: this.share()
    };
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
        else if (code === 'amount_too_high') this.payError.set(t.customTooHigh(this.money(details?.availableMinor ?? 0)));
        else if (code === 'no_items_selected') this.payError.set(t.selectionExpired);
        else if (code === 'tip_too_high') this.payError.set(t.customTipTooHigh);
        else if (code === 'equal_unavailable' || code === 'items_unavailable' || code === 'split_not_set') {
          this.selection.set({ mode: 'Full' });
          this.payError.set(code === 'equal_unavailable' ? t.equalUnavailable : code === 'items_unavailable' ? t.itemsUnavailable : t.splitChanged);
        } else if (code === 'nothing_to_pay') this.load();
        else if (code.startsWith('promo_')) {
          this.removePromo();
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

  protected setBill(bill: Bill): void {
    this.bill.set(bill);

    if (!this.defaulted) {
      this.defaulted = true;
      // The venue's "Most common" tip starts selected (P1-PAY-11); the guest can pick "Not now".
      if (bill.tips.enabled && bill.tips.mostCommonBasisPoints !== null)
        this.tipChoice.set({ kind: 'preset', basisPoints: bill.tips.mostCommonBasisPoints });
      // Carry on with a split the guest (or their table) already started.
      if (bill.split.selectedMinor > 0) this.selection.set({ mode: 'Items' });
      else if (bill.split.equalAvailable && (bill.split.sharesLeft ?? 0) > 0) this.selection.set({ mode: 'Equal', shares: 1 });
    }

    this.keepSelectionValid(bill);
    this.revalidatePromo();

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

  /** Someone else's payment or a new order can change what this guest's split covers; say so. */
  private keepSelectionValid(bill: Bill): void {
    const t = this.i18n.t();
    const selection = this.selection();
    if (bill.status !== 'Open') return;
    if (selection.mode === 'Equal') {
      const left = bill.split.sharesLeft ?? 0;
      if (!bill.split.equalAvailable || left < 1) {
        this.selection.set({ mode: 'Full' });
        this.notice.set(t.splitChanged);
      } else if (selection.shares > left) {
        this.selection.set({ mode: 'Equal', shares: left });
        this.notice.set(t.splitChanged);
      }
    } else if (selection.mode === 'Items' && bill.split.selectedMinor === 0) {
      this.selection.set({ mode: 'Full' });
      this.notice.set(t.selectionExpired);
    } else if (selection.mode === 'Custom' && selection.amountMinor > bill.availableMinor) {
      this.notice.set(t.customTooHigh(this.money(bill.availableMinor)));
    }
  }

  /** A promotion's discount depends on the share it applies to; check it again when that changes. */
  private revalidatePromo(): void {
    const promo = this.promo();
    if (!promo || this.promoBase === this.share()) return;
    this.promoText.set(promo.code);
    this.promo.set(null);
    this.promoBase = null;
    if (this.share() > 0) this.applyPromo();
  }

  private promoMessage(code: string): string {
    const t = this.i18n.t();
    return t.promoErrors[code] ?? t.promoErrors['promo_invalid']!;
  }
}
