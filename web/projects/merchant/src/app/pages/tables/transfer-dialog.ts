import { Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { FinanceApi, TableBill, TableBillLine, TableStatus, TransferResult } from '../../core/api';
import { errorMessage } from '../../core/auth';
import { I18n } from '../../core/i18n';
import { formatMoney } from '../../core/money';
import { TRANSFER } from './transfer.i18n';

type Mode = 'all' | 'some';

/**
 * Moves a table's bill to another table, whole or in part: staff pick the table, then either the
 * whole bill or how many of each item. The API decides what's allowed (money already paid stays
 * covered, items paid by item stay, nothing moves mid-payment); this shows the same rules up front.
 */
@Component({
  selector: 'hz-transfer-dialog',
  imports: [FormsModule],
  template: `
    @let s = this.s();
    @let b = bill();
    <div class="modal-backdrop" (click)="cancelled.emit()"></div>
    <form class="modal max-w-lg" role="dialog" aria-modal="true" aria-labelledby="transfer-title" (ngSubmit)="move()" (keydown.escape)="cancelled.emit()">
      <h2 id="transfer-title" class="text-lg font-bold">{{ s.title(place(from())) }}</h2>
      <p class="mt-1 text-sm text-ink-muted">{{ s.intro }}</p>

      <label class="mt-4 block">
        <span class="field-label">{{ s.to }}</span>
        <select class="field" name="to" required [ngModel]="toId()" (ngModelChange)="toId.set($event)">
          <option value="" disabled>{{ s.choose }}</option>
          @for (t of destinations(); track t.locationId) {
            <option [value]="t.locationId">{{ option(t) }}</option>
          }
        </select>
      </label>
      @if (destination(); as d) {
        @if (hasBill(d)) { <p class="mt-1 text-xs text-ink-muted">{{ s.joins(place(d)) }}</p> }
      }

      <fieldset class="mt-4">
        <legend class="field-label">{{ s.what }}</legend>
        <div class="grid gap-2 sm:grid-cols-2">
          <label class="flex cursor-pointer items-start gap-2 rounded-lg border p-3 text-sm" [class]="mode() === 'all' ? 'border-ink bg-accent-soft' : 'border-line'">
            <input type="radio" name="mode" class="mt-0.5 accent-brand" [checked]="mode() === 'all'" (change)="mode.set('all')" />
            <span><span class="block font-semibold">{{ s.wholeBill }}</span><span class="block text-xs text-ink-muted">{{ s.wholeBillHint }}</span></span>
          </label>
          <label class="flex cursor-pointer items-start gap-2 rounded-lg border p-3 text-sm" [class]="mode() === 'some' ? 'border-ink bg-accent-soft' : 'border-line'">
            <input type="radio" name="mode" class="mt-0.5 accent-brand" [checked]="mode() === 'some'" (change)="mode.set('some')" />
            <span class="font-semibold">{{ s.someItems }}</span>
          </label>
        </div>
      </fieldset>

      @if (!b) {
        <div class="mt-3 h-24 animate-pulse rounded-lg bg-surface" aria-busy="true"></div>
      } @else if (!b.lines.length) {
        <p class="mt-3 text-sm text-ink-muted">{{ s.empty }}</p>
      } @else {
        @if (mode() === 'some') {
          <ul class="mt-3 divide-y divide-line rounded-lg border border-line">
            @for (line of b.lines; track line.orderLineId) {
              @let n = picked()[line.orderLineId] ?? 0;
              <li class="flex items-center gap-3 px-3 py-2 text-sm">
                <span class="min-w-0 flex-1">
                  <span class="block font-semibold">{{ line.quantity }}× {{ name(line) }}</span>
                  @if (line.modifiers.length) { <span class="block text-xs text-ink-muted">{{ modifiers(line) }}</span> }
                  <span class="block text-xs text-ink-muted">{{ s.order(line.orderNumber) }} · {{ money(line.unitPriceMinor) }}</span>
                </span>
                @if (line.movableQuantity === 0) {
                  <span class="shrink-0 text-xs text-ink-muted">{{ s.paidByItem }}</span>
                } @else {
                  <span class="flex shrink-0 items-center gap-1">
                    <button type="button" class="size-8 rounded-lg border border-line font-bold disabled:opacity-40" [disabled]="n === 0"
                            [attr.aria-label]="s.fewer(name(line))" (click)="step(line, -1)">−</button>
                    <span class="w-12 text-center tabular-nums">{{ n }}/{{ line.movableQuantity }}</span>
                    <button type="button" class="size-8 rounded-lg border border-line font-bold disabled:opacity-40" [disabled]="n === line.movableQuantity"
                            [attr.aria-label]="s.more(name(line))" (click)="step(line, 1)">+</button>
                    @if (line.movableQuantity > 1) {
                      <button type="button" class="btn-ghost px-2 text-xs" [disabled]="n === line.movableQuantity" (click)="pickAll(line)">{{ s.all }}</button>
                    }
                  </span>
                }
              </li>
            }
          </ul>
          <p class="mt-2 text-sm" [class]="count() ? 'font-semibold' : 'text-ink-muted'" aria-live="polite">
            {{ count() ? s.selected(i18n.t().items(count()), money(amount())) : s.pickItems }}
          </p>
        }
        @if (b.paymentInProgress) {
          <p class="mt-3 rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-900" role="status">{{ s.paying }}</p>
        } @else if (paidMustStay()) {
          <p class="mt-3 rounded-lg bg-surface px-3 py-2 text-sm">{{ s.paid(money(b.paidMinor)) }}</p>
        }
      }

      @if (error()) { <p class="mt-4 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-800" role="alert">{{ error() }}</p> }
      <div class="mt-6 flex justify-end gap-2">
        <button type="button" class="btn-ghost" (click)="cancelled.emit()">{{ i18n.t().cancel }}</button>
        <button type="submit" class="btn-primary" [disabled]="!canMove()">{{ busy() ? s.moving : s.move }}</button>
      </div>
    </form>
  `
})
export class TransferDialog implements OnInit {
  private readonly api = inject(FinanceApi);
  protected readonly i18n = inject(I18n);
  protected readonly s = this.i18n.use(TRANSFER);

  /** The table whose bill moves. */
  readonly from = input.required<TableStatus>();
  /** Every table on the live view; the others are where it can go. */
  readonly tables = input.required<TableStatus[]>();
  readonly moved = output<TransferResult>();
  readonly cancelled = output<void>();

  protected readonly bill = signal<TableBill | null>(null);
  protected readonly toId = signal('');
  protected readonly mode = signal<Mode>('all');
  /** How many of each line to move, by order line id. */
  protected readonly picked = signal<Partial<Record<string, number>>>({});
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly destinations = computed(() => this.tables().filter(t => t.locationId !== this.from().locationId));
  protected readonly destination = computed(() => this.destinations().find(t => t.locationId === this.toId()) ?? null);
  protected readonly count = computed(() => Object.values(this.picked()).reduce((sum: number, n) => sum + (n ?? 0), 0));
  protected readonly amount = computed(() =>
    (this.bill()?.lines ?? []).reduce((sum, l) => sum + (this.picked()[l.orderLineId] ?? 0) * l.unitPriceMinor, 0)
  );

  /** Paid money stays behind unless the whole bill goes to a table without one, where it moves too. */
  protected readonly paidMustStay = computed(() => {
    const destination = this.destination();
    return (this.bill()?.paidMinor ?? 0) > 0 && (this.mode() === 'some' || (!!destination && this.hasBill(destination)));
  });

  protected readonly canMove = computed(() => {
    const bill = this.bill();
    return !this.busy() && !!this.toId() && !!bill?.lines.length && !bill.paymentInProgress && (this.mode() === 'all' || this.count() > 0);
  });

  ngOnInit(): void {
    this.api.tableBill(this.from().locationId).subscribe({
      next: bill => this.bill.set(bill),
      error: error => this.error.set(errorMessage(error))
    });
  }

  /** Has an open bill with orders on it, which what's moved will join. */
  protected hasBill(t: TableStatus): boolean {
    return t.state === 'Unpaid' || t.state === 'PartiallyPaid' || t.state === 'Attention';
  }

  /** "Table 12" in the portal's language. */
  protected place(t: TableStatus): string {
    return `${this.i18n.t().locationTypes[t.locationType] ?? t.locationType} ${t.label}`;
  }

  protected option(t: TableStatus): string {
    const area = t.areaName ? ` · ${t.areaName}` : '';
    return `${this.place(t)}${area}${this.hasBill(t) ? ` (${this.s().hasBill})` : ''}`;
  }

  protected name(line: TableBillLine): string {
    return (this.i18n.lang() === 'ar' && line.nameAr) || line.name;
  }

  protected modifiers(line: TableBillLine): string {
    const ar = this.i18n.lang() === 'ar';
    return line.modifiers.map(m => (ar && m.nameAr) || m.name).join(' · ');
  }

  protected money(minor: number): string {
    return formatMoney(minor, this.bill()?.currency ?? this.from().currency);
  }

  protected step(line: TableBillLine, by: number): void {
    const next = Math.min(line.movableQuantity, Math.max(0, (this.picked()[line.orderLineId] ?? 0) + by));
    this.picked.update(picked => ({ ...picked, [line.orderLineId]: next }));
  }

  protected pickAll(line: TableBillLine): void {
    this.picked.update(picked => ({ ...picked, [line.orderLineId]: line.movableQuantity }));
  }

  protected move(): void {
    if (!this.canMove()) return;
    const lines = this.mode() === 'all'
      ? null
      : this.bill()!.lines.map(l => ({ orderLineId: l.orderLineId, quantity: this.picked()[l.orderLineId] ?? 0 })).filter(l => l.quantity > 0);

    this.busy.set(true);
    this.error.set(null);
    this.api.transfer(this.from().locationId, this.toId(), lines).subscribe({
      next: result => {
        this.busy.set(false);
        this.moved.emit(result);
      },
      error: error => {
        this.busy.set(false);
        this.error.set(errorMessage(error));
      }
    });
  }
}
