import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { merge } from 'rxjs';

import { FinanceApi, TableState, TableStatus } from '../../core/api';
import { errorMessage } from '../../core/auth';
import { CurrentVenue } from '../../core/current-venue';
import { I18n } from '../../core/i18n';
import { LiveOrders } from '../../core/live-orders';
import { formatMoney } from '../../core/money';
import { TABLES } from './tables.i18n';

type Filter = 'All' | 'Unpaid' | 'PartiallyPaid' | 'Paid';

/**
 * The live table view (blueprint 4.4 "Tables", P1-MER-10, mockup "Merchant table view"): each
 * table's state, what's been paid against its bill, and its orders when expanded. Updates as
 * orders and payments come in.
 */
@Component({
  selector: 'df-live-tables',
  template: `
    @let s = this.s();
    <div class="flex flex-wrap items-center gap-2" role="group" [attr.aria-label]="s.show">
      @for (f of filters; track f) {
        <button type="button" class="rounded-full border px-4 py-2 text-sm font-semibold" [attr.aria-pressed]="filter() === f"
                [class]="filter() === f ? 'border-ink bg-ink text-white' : 'border-line bg-white text-ink'" (click)="filter.set(f)">
          {{ s.filters[f] }} <span class="ms-1 opacity-70">{{ count(f) }}</span>
        </button>
      }
    </div>

    @if (error()) {
      <p class="mt-4 rounded-lg bg-red-50 px-4 py-3 text-sm text-red-800" role="alert">{{ error() }}</p>
    }

    <ul class="mt-4 grid gap-2.5 lg:grid-cols-2">
      @for (t of shown(); track t.locationId) {
        @let open = expanded() === t.locationId;
        <li class="rounded-xl border bg-white" [class]="t.state === 'Attention' ? 'border-red-300' : 'border-line'">
          <button type="button" class="flex w-full items-center gap-3 px-4 py-3 text-start" [attr.aria-expanded]="open" [disabled]="!t.checkId"
                  (click)="expanded.set(open ? null : t.locationId)">
            <span class="flex size-10 shrink-0 items-center justify-center rounded-lg bg-surface text-sm font-bold">{{ t.label }}</span>
            <span class="min-w-0 flex-1">
              <span class="block font-semibold">{{ place(t) }}</span>
              <span class="block text-xs text-ink-muted">{{ t.areaName ?? i18n.t().noArea }}@if (t.lastActivityAt) { · {{ ago(t.lastActivityAt) }} }</span>
            </span>
            <span class="shrink-0 text-end">
              @if (t.checkId) { <span class="block text-sm font-semibold tabular-nums">{{ money(t.paidMinor, t.currency) }} / {{ money(t.totalMinor, t.currency) }}</span> }
              <span class="mt-0.5 inline-flex items-center gap-1 rounded-md px-2 py-0.5 text-xs font-bold" [class]="badgeClass(t.state)">
                {{ badgeIcon(t.state) }} {{ s.states[t.state] }}
              </span>
            </span>
          </button>
          @if (open) {
            <div class="border-t border-line px-4 py-3 text-sm">
              @if (t.attentionReason) { <p class="mb-2 rounded-lg bg-red-50 px-3 py-2 text-red-800">{{ s.paymentFailed }}</p> }
              <dl class="grid grid-cols-[140px_1fr] gap-y-1">
                <dt class="text-ink-muted">{{ s.customersPaid }}</dt>
                <dd>{{ money(t.paidMinor, t.currency) }}@if (t.tipsMinor) { <span class="text-ink-muted"> ({{ s.tip(money(t.tipsMinor, t.currency)) }})</span> }</dd>
                <dt class="text-ink-muted">{{ s.remaining }}</dt><dd class="font-semibold">{{ money(t.remainingMinor, t.currency) }}</dd>
              </dl>
              <table class="mt-3 w-full">
                <thead class="text-xs text-ink-muted uppercase"><tr><th scope="col" class="py-1 text-start font-semibold">{{ s.order }}</th><th scope="col" class="py-1 text-start font-semibold">{{ s.created }}</th><th scope="col" class="py-1 text-start font-semibold">{{ s.status }}</th><th scope="col" class="py-1 text-end font-semibold">{{ s.total }}</th></tr></thead>
                <tbody>
                  @for (o of t.orders; track o.id) {
                    <tr class="border-t border-line">
                      <td class="py-1.5 font-semibold">#{{ o.number }}</td>
                      <td class="py-1.5 text-ink-muted">{{ time(o.createdAt) }}</td>
                      <td class="py-1.5">{{ s.orderStatuses[o.status] || o.status }}</td>
                      <td class="py-1.5 text-end tabular-nums">{{ money(o.totalMinor, t.currency) }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        </li>
      } @empty {
        <li class="text-sm text-ink-muted">{{ s.empty }}</li>
      }
    </ul>
  `
})
export class LiveTables implements OnInit {
  private readonly api = inject(FinanceApi);
  private readonly venue = inject(CurrentVenue);
  private readonly live = inject(LiveOrders);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly i18n = inject(I18n);
  private readonly strings = this.i18n.use(TABLES);
  /** Named "s" because the template's loop variable is "t". */
  protected readonly s = computed(() => this.strings().live);
  protected readonly filters: Filter[] = ['All', 'Unpaid', 'PartiallyPaid', 'Paid'];
  protected readonly filter = signal<Filter>('All');
  protected readonly tables = signal<TableStatus[]>([]);
  protected readonly expanded = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  private readonly now = signal(Date.now());

  protected readonly shown = computed(() => this.tables().filter(t => this.matches(t, this.filter())));

  ngOnInit(): void {
    this.venue.load().subscribe({ next: () => this.load(), error: error => this.error.set(errorMessage(error)) });
    merge(this.live.placed, this.live.changed, this.live.paid, this.live.reconnected)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.load());
    // Keep "12 min ago" current and pick up tables guests have just scanned.
    const timer = setInterval(() => {
      this.now.set(Date.now());
      this.load();
    }, 60_000);
    this.destroyRef.onDestroy(() => clearInterval(timer));
  }

  protected count(filter: Filter): number {
    return this.tables().filter(t => this.matches(t, filter)).length;
  }

  protected badgeIcon(state: TableState): string {
    return { Available: '', Ordering: '…', Unpaid: '!', PartiallyPaid: '◐', Paid: '✓', Attention: '⚠' }[state];
  }

  protected badgeClass(state: TableState): string {
    switch (state) {
      case 'Paid': return 'bg-emerald-50 text-emerald-700';
      case 'PartiallyPaid': return 'bg-amber-50 text-amber-800';
      case 'Unpaid': return 'bg-red-50 text-red-800';
      case 'Attention': return 'bg-red-600 text-white';
      case 'Ordering': return 'bg-brand/10 text-brand';
      default: return 'bg-surface text-ink-muted';
    }
  }

  /** "Table 12" in the portal's language (the API's displayName is English). */
  protected place(t: TableStatus): string {
    return `${this.i18n.t().locationTypes[t.locationType] ?? t.locationType} ${t.label}`;
  }

  protected money(minor: number, currency: string): string {
    return formatMoney(minor, currency);
  }

  protected ago(iso: string): string {
    const minutes = Math.max(0, Math.round((this.now() - new Date(iso).getTime()) / 60_000));
    const s = this.s();
    if (minutes < 1) return s.justNow;
    if (minutes < 60) return s.minutesAgo(minutes);
    return s.hoursAgo(Math.floor(minutes / 60));
  }

  protected time(iso: string): string {
    return new Date(iso).toLocaleTimeString(this.i18n.locale(), { hour: '2-digit', minute: '2-digit' });
  }

  private matches(t: TableStatus, filter: Filter): boolean {
    switch (filter) {
      case 'Unpaid': return t.state === 'Unpaid' || t.state === 'Attention';
      case 'PartiallyPaid': return t.state === 'PartiallyPaid';
      case 'Paid': return t.state === 'Paid';
      default: return true;
    }
  }

  private load(): void {
    const venueId = this.venue.venue()?.id;
    if (!venueId) return;
    this.api.tableStatus(venueId).subscribe({ next: t => this.tables.set(t), error: error => this.error.set(errorMessage(error)) });
  }
}
