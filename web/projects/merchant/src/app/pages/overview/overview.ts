import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { merge } from 'rxjs';

import { FinanceApi, Overview as OverviewData, OverviewPeriod, TrendGranularity } from '../../core/api';
import { errorMessage } from '../../core/auth';
import { CurrentVenue } from '../../core/current-venue';
import { I18n } from '../../core/i18n';
import { LiveOrders } from '../../core/live-orders';
import { formatMoney } from '../../core/money';
import { TrendChart, TrendValue } from '../../shared/trend-chart';
import { OVERVIEW } from './overview.i18n';

type AttentionItem = OverviewData['attention'][number];

/**
 * The merchant overview (blueprint 4.4, P1-MER-08/09, mockup "Merchant overview"): the chosen
 * period and today at a glance, the last week against the week before, a revenue or tips trend,
 * and what needs attention. Updates live as orders and payments come in.
 */
@Component({
  selector: 'df-overview',
  imports: [RouterLink, TrendChart],
  templateUrl: './overview.html'
})
export class Overview implements OnInit {
  private readonly api = inject(FinanceApi);
  private readonly venue = inject(CurrentVenue);
  private readonly live = inject(LiveOrders);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly i18n = inject(I18n);
  protected readonly t = this.i18n.use(OVERVIEW);

  protected readonly periods: OverviewPeriod[] = ['Today', 'Week', 'Month', 'Quarter'];
  protected readonly granularities: TrendGranularity[] = ['Hourly', 'Daily', 'Weekly', 'Monthly'];
  protected readonly period = signal<OverviewPeriod>('Month');
  protected readonly granularity = signal<TrendGranularity | null>(null);
  protected readonly series = signal<'revenue' | 'tips'>('revenue');
  protected readonly data = signal<OverviewData | null>(null);
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly periodName = computed(() => this.t().periodNames[this.period()]);
  protected readonly trend = computed<TrendValue[]>(() =>
    (this.data()?.trend ?? []).map(p => ({ start: p.start, valueMinor: this.series() === 'revenue' ? p.revenueMinor : p.tipsMinor })));

  ngOnInit(): void {
    this.venue.load().subscribe({ next: () => this.load(), error: error => this.error.set(errorMessage(error)) });
    merge(this.live.placed, this.live.changed, this.live.paid, this.live.refunded, this.live.reviewed, this.live.linkPaid)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.load());
  }

  protected setPeriod(value: string): void {
    this.period.set(value as OverviewPeriod);
    this.granularity.set(null);
    this.load();
  }

  protected setGranularity(value: string): void {
    this.granularity.set(value as TrendGranularity);
    this.load();
  }

  /** An item's title and detail in the portal's language; an item the portal doesn't know keeps the API's English. */
  protected attention(a: AttentionItem): { title: string; detail: string } {
    const word = a.code ? this.t().attention[a.code] : undefined;
    if (!word) return { title: a.title, detail: a.detail };
    const p = a.params ?? {};
    const type = String(p['locationType'] ?? '');
    return word({
      count: a.count,
      place: `${this.i18n.t().locationTypes[type] ?? type} ${p['label'] ?? ''}`.trim(),
      linkCode: typeof p['linkCode'] === 'string' ? p['linkCode'] : null,
      time: String(p['time'] ?? ''),
      refused: Number(p['refused'] ?? 0),
      open: this.money(Number(p['openMinor'] ?? 0)),
      minutes: Number(p['minutes'] ?? 0)
    });
  }

  protected money(minor: number): string {
    return formatMoney(minor, this.data()?.currency ?? this.venue.venue()?.currency ?? 'AED');
  }

  /** "▲ 5.57% vs last week". */
  protected revenueChange(d: OverviewData): string | null {
    const change = d.week.revenueChange;
    if (change === null) return null;
    return this.t().vsLastWeek(`${change > 0 ? '▲' : change < 0 ? '▼' : '='} ${Math.abs(change * 100).toFixed(2)}%`);
  }

  protected ordersChange(d: OverviewData): string {
    const diff = d.week.orders - d.week.previousOrders;
    return this.t().vsLastWeek(`${diff > 0 ? '▲' : diff < 0 ? '▼' : '='} ${Math.abs(diff)}`);
  }

  protected link(kind: string): string {
    return { payment: '/transactions', refund: '/transactions', tables: '/tables', review: '/reviews', orders: '/orders', pos: '/settings', reconciliation: '/reports' }[kind] ?? '/overview';
  }

  private load(): void {
    const venueId = this.venue.venue()?.id;
    if (!venueId) return;
    this.loading.set(true);
    this.api.overview(venueId, this.period(), this.granularity() ?? undefined).subscribe({
      next: d => {
        this.loading.set(false);
        this.error.set(null);
        this.data.set(d);
      },
      error: error => {
        this.loading.set(false);
        this.error.set(errorMessage(error));
      }
    });
  }
}
