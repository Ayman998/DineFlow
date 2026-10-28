import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, input, output, signal } from '@angular/core';

import { GuestSession, errorCode } from '../../core/guest-session';
import { I18n } from '../../core/i18n';
import { formatMoney, minorDigits } from '../../core/menu';
import { Bill, BillUnit, PaymentMode, PaymentsApi, Selection, UnitRef, equalShare, parseAmount, sameUnit } from '../../core/payments';

const MAX_PEOPLE = 50;

/**
 * "Split the bill" (blueprint 3.2, mockups "Split the bill: equal" and "…: by item"): the guest
 * pays the rest, an equal share for one or more people, their own items, or an amount. Picked
 * items are held on the server straight away so nobody else can pay for them too.
 */
@Component({
  selector: 'df-split-sheet',
  templateUrl: './split-sheet.html'
})
export class SplitSheet implements OnInit {
  private readonly api = inject(PaymentsApi);
  private readonly session = inject(GuestSession);
  protected readonly i18n = inject(I18n);

  readonly bill = input.required<Bill>();
  readonly current = input.required<Selection>();
  readonly confirmed = output<Selection>();
  readonly removed = output<void>();
  readonly closed = output<void>();
  /** Holding items or changing the split changed the bill; the checkout shows the new one. */
  readonly billChange = output<Bill>();

  protected readonly tabs: PaymentMode[] = ['Full', 'Equal', 'Items', 'Custom'];
  protected readonly tab = signal<PaymentMode>('Full');
  protected readonly people = signal(2);
  protected readonly shares = signal(1);
  protected readonly amountText = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly minPeople = computed(() => Math.max(2, this.bill().split.sharesTaken + 1));
  protected readonly maxPeople = MAX_PEOPLE;
  protected readonly sharesLeft = computed(() => this.people() - this.bill().split.sharesTaken);
  protected readonly equalAmount = computed(() => equalShare(this.bill().availableMinor, this.sharesLeft(), this.shares()));
  protected readonly myItems = computed(() => this.bill().units.filter(u => u.mine && u.status === 'Selected'));
  protected readonly amount = computed(() => parseAmount(this.amountText(), this.bill().currency));

  protected readonly equalLocked = computed(() => !this.bill().split.equalAvailable);
  protected readonly itemsLocked = computed(() => !this.bill().split.itemsAvailable && this.bill().split.selectedMinor === 0);

  /** What Confirm would have this guest pay. */
  protected readonly yourShare = computed(() => {
    const bill = this.bill();
    switch (this.tab()) {
      case 'Full': return bill.availableMinor;
      case 'Equal': return this.equalAmount();
      case 'Items': return bill.split.selectedMinor;
      case 'Custom': {
        const amount = this.amount();
        return amount && 'minor' in amount ? Math.min(amount.minor, bill.availableMinor) : 0;
      }
    }
  });

  protected readonly canConfirm = computed(() => {
    if (this.busy()) return false;
    switch (this.tab()) {
      case 'Equal': return !this.equalLocked() && this.sharesLeft() >= 1 && this.equalAmount() > 0;
      case 'Items': return this.bill().split.selectedMinor > 0;
      case 'Custom': {
        const amount = this.amount();
        return !!amount && 'minor' in amount && amount.minor > 0 && amount.minor <= this.bill().availableMinor;
      }
      default: return this.bill().availableMinor > 0;
    }
  });

  ngOnInit(): void {
    const bill = this.bill();
    const current = this.current();
    this.tab.set(current.mode);
    this.people.set(Math.max(this.minPeople(), bill.split.people ?? 2));
    this.shares.set(current.mode === 'Equal' ? Math.min(current.shares, this.sharesLeft()) : 1);
    if (current.mode === 'Custom') this.amountText.set(String(current.amountMinor / 10 ** minorDigits(bill.currency)));
  }

  protected money(minor: number): string {
    return formatMoney(minor, this.bill().currency, this.i18n.lang());
  }

  protected tabLabel(mode: PaymentMode): string {
    const t = this.i18n.t();
    return { Full: t.tabFull, Equal: t.tabEqual, Items: t.tabItems, Custom: t.tabCustom }[mode];
  }

  protected tabDisabled(mode: PaymentMode): boolean {
    return (mode === 'Equal' && this.equalLocked()) || (mode === 'Items' && this.itemsLocked());
  }

  protected setPeople(value: number): void {
    const people = Math.min(MAX_PEOPLE, Math.max(this.minPeople(), value));
    this.people.set(people);
    this.shares.set(Math.min(this.shares(), people - this.bill().split.sharesTaken));
  }

  protected setShares(value: number): void {
    this.shares.set(Math.min(this.sharesLeft(), Math.max(1, value)));
  }

  protected unitName(unit: BillUnit): string {
    return this.i18n.localize(unit.name, unit.nameAr);
  }

  protected unitOptions(unit: BillUnit): string {
    return unit.options.map((name, i) => this.i18n.localize(name, unit.optionsAr?.[i])).join(' · ');
  }

  /** What the row shows instead of a button when the item isn't this guest's to pick. */
  protected lockText(unit: BillUnit): string | null {
    const t = this.i18n.t();
    if (unit.status === 'Open' || (unit.status === 'Selected' && unit.mine)) return null;
    if (unit.status === 'Selected') return t.selectedByOther;
    if (unit.status === 'Paying') return unit.mine ? t.yourPaymentInProgress : t.beingPaidByOther;
    return unit.mine ? t.paidByYou : t.paidByOther;
  }

  /** Picks or drops one item. The whole selection is sent, and the server holds it. */
  protected toggle(unit: BillUnit): void {
    const sessionId = this.session.current()?.context.sessionId;
    if (!sessionId || this.busy()) return;
    const selected = unit.mine && unit.status === 'Selected';
    const refs: UnitRef[] = this.myItems().filter(u => !sameUnit(u, unit)).map(toRef);
    if (!selected) refs.push(toRef(unit));

    this.busy.set(true);
    this.error.set(null);
    this.api.reserve(sessionId, refs).subscribe({
      next: result => {
        this.busy.set(false);
        this.billChange.emit(result.bill);
        if (result.taken.length) this.error.set(this.i18n.t().itemTaken);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.fail(error);
      }
    });
  }

  protected confirm(): void {
    if (!this.canConfirm()) return;
    const bill = this.bill();
    switch (this.tab()) {
      case 'Items':
        this.confirmed.emit({ mode: 'Items' });
        return;
      case 'Custom': {
        const amount = this.amount();
        if (amount && 'minor' in amount) this.done({ mode: 'Custom', amountMinor: amount.minor });
        return;
      }
      case 'Equal': {
        const shares = this.shares();
        if (bill.split.people === this.people()) {
          this.done({ mode: 'Equal', shares });
          return;
        }
        // Everyone at the table shares the same split, so the head count is saved on the bill.
        this.busy.set(true);
        this.api.setSplit(this.sessionId(), this.people()).subscribe({
          next: updated => {
            this.busy.set(false);
            this.billChange.emit(updated);
            this.done({ mode: 'Equal', shares });
          },
          error: (error: unknown) => {
            this.busy.set(false);
            this.fail(error);
          }
        });
        return;
      }
      default:
        this.done({ mode: 'Full' });
    }
  }

  /** Back to paying the full remaining bill (blueprint "remove a split"). */
  protected remove(): void {
    const bill = this.bill();
    this.busy.set(true);
    const finish = () => {
      this.busy.set(false);
      this.removed.emit();
    };
    this.releaseItems(() => {
      if (bill.split.people !== null && bill.split.sharesTaken === 0) {
        this.api.setSplit(this.sessionId(), null).subscribe({
          next: updated => {
            this.billChange.emit(updated);
            finish();
          },
          error: finish
        });
      } else {
        finish();
      }
    });
  }

  /** Confirms an amount-based choice, letting go of any items this guest had picked. */
  private done(selection: Selection): void {
    this.busy.set(true);
    this.releaseItems(() => {
      this.busy.set(false);
      this.confirmed.emit(selection);
    });
  }

  private releaseItems(then: () => void): void {
    if (!this.myItems().length) {
      then();
      return;
    }
    this.api.reserve(this.sessionId(), []).subscribe({
      next: result => {
        this.billChange.emit(result.bill);
        then();
      },
      error: then
    });
  }

  private fail(error: unknown): void {
    const t = this.i18n.t();
    const details = error instanceof HttpErrorResponse ? (error.error?.details as Bill | undefined) : undefined;
    if (details) this.billChange.emit(details);
    const code = errorCode(error);
    this.error.set(
      code === 'equal_unavailable' ? t.equalUnavailable
        : code === 'items_unavailable' ? t.itemsUnavailable
          : code === 'invalid_split' && error instanceof HttpErrorResponse ? (error.error?.title ?? t.splitFailed)
            : t.splitFailed);
  }

  private sessionId(): string {
    return this.session.current()?.context.sessionId ?? '';
  }
}

function toRef(unit: UnitRef): UnitRef {
  return { orderLineId: unit.orderLineId, unitIndex: unit.unitIndex };
}
