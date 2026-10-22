import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { merge } from 'rxjs';

import { FinanceApi, TransactionDetail, TransactionFilter, TransactionPage, TransactionRow, TransactionState } from '../../core/api';
import { Auth, errorMessage } from '../../core/auth';
import { CurrentVenue } from '../../core/current-venue';
import { LiveOrders } from '../../core/live-orders';
import { formatMoney, parseMajor, toMajorString } from '../../core/money';

const DAY = 24 * 60 * 60 * 1000;

/**
 * Transactions (blueprint 4.4, 9.2 /v1/merchant/transactions/*): search payments by reference,
 * table or amount, open one to see its breakdown and complete timeline, and refund it in full
 * or in part (owners and managers). A refund completes when the provider confirms it.
 */
@Component({
  selector: 'df-transactions',
  templateUrl: './transactions.html'
})
export class Transactions implements OnInit {
  private readonly api = inject(FinanceApi);
  private readonly venue = inject(CurrentVenue);
  private readonly live = inject(LiveOrders);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly auth = inject(Auth);

  protected readonly ranges = [1, 7, 30, 90];
  protected readonly filters: TransactionFilter[] = ['All', 'Succeeded', 'Refunded', 'Pending', 'Failed'];
  protected readonly days = signal(30);
  protected readonly status = signal<TransactionFilter>('All');
  protected readonly search = signal('');
  protected readonly page = signal(1);
  protected readonly data = signal<TransactionPage | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly loading = signal(false);

  protected readonly detail = signal<TransactionDetail | null>(null);
  protected readonly refundOpen = signal(false);
  protected readonly refundAmount = signal('');
  protected readonly refundReason = signal('');
  protected readonly refunding = signal(false);
  protected readonly refundError = signal<string | null>(null);
  private refundRequestId: string | null = null;
  private searchTimer: ReturnType<typeof setTimeout> | undefined;

  ngOnInit(): void {
    this.venue.load().subscribe({ next: () => this.load(), error: error => this.error.set(errorMessage(error)) });
    merge(this.live.paid, this.live.refunded, this.live.linkPaid).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.load();
      const open = this.detail();
      if (open) this.open(open.summary.id);
    });
    this.destroyRef.onDestroy(() => clearTimeout(this.searchTimer));
  }

  protected setRange(days: number): void {
    this.days.set(days);
    this.reload();
  }

  protected setStatus(status: TransactionFilter): void {
    this.status.set(status);
    this.reload();
  }

  protected setSearch(text: string): void {
    this.search.set(text);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.reload(), 300);
  }

  protected goTo(page: number): void {
    this.page.set(page);
    this.load();
  }

  protected open(id: string): void {
    this.api.transaction(id).subscribe({ next: d => this.detail.set(d), error: error => this.error.set(errorMessage(error)) });
  }

  protected close(): void {
    this.detail.set(null);
    this.refundOpen.set(false);
  }

  protected startRefund(): void {
    const d = this.detail();
    if (!d) return;
    this.refundAmount.set(toMajorString(d.refundableMinor, d.summary.currency));
    this.refundReason.set('');
    this.refundError.set(null);
    this.refundRequestId = crypto.randomUUID();
    this.refundOpen.set(true);
  }

  protected submitRefund(): void {
    const d = this.detail();
    if (!d || this.refunding()) return;
    const amount = parseMajor(this.refundAmount(), d.summary.currency);
    if (!amount) return this.refundError.set(`Enter an amount in ${d.summary.currency}, e.g. 12.50.`);
    if (amount > d.refundableMinor) return this.refundError.set(`You can refund up to ${this.money(d.refundableMinor, d.summary.currency)}.`);
    if (!this.refundReason().trim()) return this.refundError.set('Add a reason; it goes in the audit log.');

    this.refunding.set(true);
    this.refundError.set(null);
    this.api.refund(d.summary.id, this.refundRequestId ?? crypto.randomUUID(), amount, this.refundReason().trim()).subscribe({
      next: updated => {
        this.refunding.set(false);
        this.refundOpen.set(false);
        this.detail.set(updated);
        this.load();
      },
      error: error => {
        this.refunding.set(false);
        this.refundError.set(errorMessage(error));
      }
    });
  }

  protected stateLabel(state: TransactionState): string {
    return { Pending: 'Pending', Succeeded: 'Paid', PartiallyRefunded: 'Partly refunded', Refunded: 'Refunded', Failed: 'Failed', Cancelled: 'Cancelled' }[state];
  }

  protected stateClass(state: TransactionState): string {
    switch (state) {
      case 'Succeeded': return 'bg-emerald-50 text-emerald-700';
      case 'PartiallyRefunded':
      case 'Refunded': return 'bg-surface text-ink';
      case 'Failed': return 'bg-red-50 text-red-800';
      case 'Pending': return 'bg-amber-50 text-amber-800';
      default: return 'bg-surface text-ink-muted';
    }
  }

  protected modeText(d: TransactionDetail): string | null {
    switch (d.summary.mode) {
      case 'Equal': return `${d.shares} of ${d.splitPeople} shares`;
      case 'Items': return d.itemCount === 1 ? 'By item · 1 item' : `By item · ${d.itemCount} items`;
      case 'Custom': return 'Custom amount';
      default: return null;
    }
  }

  /** Where it was paid: the table, or the payment link. */
  protected place(row: TransactionRow): string {
    return row.paymentLinkCode ? `Link ${row.paymentLinkCode}` : `${row.locationType} ${row.locationLabel}`;
  }

  protected money(minor: number, currency: string): string {
    return formatMoney(minor, currency);
  }

  protected when(iso: string | null): string {
    return iso ? new Date(iso).toLocaleString('en-GB', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' }) : '–';
  }

  protected lastRow(d: TransactionPage): number {
    return Math.min(d.total, d.page * d.pageSize);
  }

  private reload(): void {
    this.page.set(1);
    this.load();
  }

  private load(): void {
    const venueId = this.venue.venue()?.id;
    if (!venueId) return;
    const to = new Date();
    this.loading.set(true);
    this.api.transactions({
      venueId,
      from: new Date(to.getTime() - this.days() * DAY).toISOString(),
      to: to.toISOString(),
      status: this.status(),
      search: this.search().trim() || undefined,
      page: this.page(),
      pageSize: 25
    }).subscribe({
      next: page => {
        this.loading.set(false);
        this.error.set(null);
        this.data.set(page);
      },
      error: error => {
        this.loading.set(false);
        this.error.set(errorMessage(error));
      }
    });
  }
}
