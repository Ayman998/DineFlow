import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { EMPTY, Subject, catchError, map, switchMap } from 'rxjs';

import { FinanceApi, ORDER_PAGE_SIZE, OrderDto, OrderFilter, OrderPeriod, OrderQuery, OrderStatus, OrdersAdminApi, TableStatus } from '../../core/api';
import { errorMessage } from '../../core/auth';
import { CurrentVenue } from '../../core/current-venue';
import { I18n } from '../../core/i18n';
import { LiveOrders } from '../../core/live-orders';
import { formatMoney } from '../../core/money';
import { ORDERS } from './orders.i18n';
import { Day, dayAt, periodLabel, periodRange, shiftPeriod } from './periods';

/** The kanban columns; their titles are in the lexicon. Each one is also a filter. */
type Column = 'New' | 'Ready' | 'Served';
const COLUMNS: Column[] = ['New', 'Ready', 'Served'];

/** Accepted and Preparing are only on orders taken on before the three-step flow; they wait in New. */
const COLUMN_OF: Partial<Record<OrderStatus, Column>> = {
  Submitted: 'New',
  Accepted: 'New',
  Preparing: 'New',
  Ready: 'Ready',
  Completed: 'Served'
};

/** Where an order goes next; its button is labelled by that status. */
const NEXT: Record<OrderStatus, OrderStatus | null> = {
  Submitted: 'Ready',
  Accepted: 'Ready',
  Preparing: 'Ready',
  Ready: 'Completed',
  Completed: null,
  Rejected: null,
  Closed: null
};

/** Served, closed and rejected orders are looked back on, newest first; the kitchen works oldest first. */
const isDone = (order: OrderDto) => order.status === 'Completed' || order.status === 'Closed' || order.status === 'Rejected';

/**
 * The live order queue (blueprint 4.4 "Orders", P1-ORD-03/04): new orders arrive in real time
 * and staff move them New → Ready → Served, or reject them with a reason the guest sees. The
 * Served column shows what was served today; once the table pays in full, its served orders move
 * to Closed. Any list can be narrowed to a day, week, month, quarter or year, a table, or an
 * order number.
 */
@Component({
  selector: 'hz-orders',
  imports: [FormsModule, NgTemplateOutlet],
  templateUrl: './orders.html'
})
export class Orders implements OnInit {
  private readonly api = inject(OrdersAdminApi);
  private readonly finance = inject(FinanceApi);
  private readonly venue = inject(CurrentVenue);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly live = inject(LiveOrders);
  protected readonly i18n = inject(I18n);
  protected readonly t = this.i18n.use(ORDERS);

  protected readonly columns = COLUMNS;
  protected readonly filters: OrderFilter[] = ['Active', 'New', 'Ready', 'Served', 'Closed', 'Rejected', 'All'];
  protected readonly periods: OrderPeriod[] = ['All', 'Day', 'Week', 'Month', 'Quarter', 'Year'];
  protected readonly filter = signal<OrderFilter>('Active');
  protected readonly orders = signal<OrderDto[] | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly loading = signal(false);
  protected readonly busy = signal<ReadonlySet<string>>(new Set());
  protected readonly fresh = signal<ReadonlySet<string>>(new Set());
  protected readonly rejecting = signal<OrderDto | null>(null);
  protected rejectReason = '';

  protected readonly period = signal<OrderPeriod>('All');
  /** A day in the chosen period; null follows today at the venue. */
  protected readonly day = signal<Day | null>(null);
  protected readonly locationId = signal('');
  /** What's typed in the order number box. */
  protected readonly numberText = signal('');
  /** The venue's tables, for the table filter. */
  protected readonly tables = signal<TableStatus[]>([]);
  /** The last page loaded, and whether it was full (so there may be more). */
  private readonly page = signal(1);
  protected readonly more = signal(false);

  /** Re-renders "x min ago" every 30 seconds. */
  protected readonly now = signal(Date.now());

  private readonly requests = new Subject<{ query: OrderQuery; append: boolean }>();
  private numberTimer: ReturnType<typeof setTimeout> | undefined;

  private readonly timeZone = computed(() => this.venue.venue()?.timeZone ?? 'Asia/Amman');
  private readonly today = computed(() => dayAt(new Date(this.now()), this.timeZone()));
  /** A day in the period on screen. */
  protected readonly anchor = computed(() => this.day() ?? this.today());
  protected readonly number = computed(() => {
    const n = Number(this.numberText().trim().replace(/^#/, ''));
    return Number.isInteger(n) && n > 0 ? n : null;
  });
  protected readonly filtered = computed(() => this.period() !== 'All' || !!this.locationId() || this.number() !== null);

  protected readonly periodText = computed(() => {
    const period = this.period();
    return period === 'All' ? '' : periodLabel(period, this.anchor(), this.i18n.locale(), this.t().quarter);
  });
  /** The period on screen includes today, so there's nothing after it yet. */
  protected readonly atLatest = computed(() => {
    const period = this.period();
    return period !== 'All' && periodRange(period, this.anchor()).end > this.today();
  });

  /** Tables by area, in the venue's order. */
  protected readonly tableGroups = computed(() => {
    const groups: { area: string; tables: TableStatus[] }[] = [];
    for (const table of this.tables()) {
      const area = table.areaName ?? '';
      const group = groups.find(g => g.area === area);
      if (group) group.tables.push(table);
      else groups.push({ area, tables: [table] });
    }
    return groups;
  });

  protected readonly byColumn = computed(() => {
    const groups = new Map<Column, OrderDto[]>();
    for (const order of this.orders() ?? []) {
      const column = COLUMN_OF[order.status];
      if (column) groups.set(column, [...(groups.get(column) ?? []), order]);
    }
    return groups;
  });

  ngOnInit(): void {
    // The latest request wins, so a quick change of filters can't leave an older answer on screen.
    this.requests
      .pipe(
        switchMap(({ query, append }) => {
          const venueId = this.venue.venue()?.id;
          if (!venueId) return EMPTY;
          this.loading.set(true);
          return this.api.list(venueId, query).pipe(
            map(orders => ({ orders, append, page: query.page ?? 1 })),
            catchError(error => {
              this.loading.set(false);
              this.error.set(errorMessage(error));
              return EMPTY;
            })
          );
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe(({ orders, append, page }) => {
        this.loading.set(false);
        this.page.set(page);
        // The board shows everything the kitchen has; lists come a page at a time.
        this.more.set(this.filter() !== 'Active' && orders.length === ORDER_PAGE_SIZE);
        this.orders.update(list => (append ? [...(list ?? []), ...orders.filter(o => !list?.some(l => l.id === o.id))] : orders));
      });

    this.venue.load().subscribe(venue => {
      this.load();
      if (venue) this.finance.tableStatus(venue.id).subscribe({ next: tables => this.tables.set(tables), error: () => undefined });
    });

    this.live.placed.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(order => {
      if (!this.fitsFilter(order)) return;
      this.upsert(order);
      this.highlight(order.id);
    });
    this.live.changed.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(order => this.upsert(order));
    this.live.reconnected.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.load());

    const timer = setInterval(() => this.now.set(Date.now()), 30_000);
    this.destroyRef.onDestroy(() => {
      clearInterval(timer);
      clearTimeout(this.numberTimer);
    });
  }

  protected setFilter(filter: OrderFilter): void {
    this.filter.set(filter);
    this.orders.set(null); // another stage's orders mustn't show under this one while it loads
    this.load();
  }

  protected setPeriod(period: OrderPeriod): void {
    this.period.set(period);
    this.day.set(null);
    this.load();
  }

  /** Moves to the period before (-1) or after (1) the one on screen. */
  protected step(step: -1 | 1): void {
    const period = this.period();
    if (period === 'All') return;
    const day = shiftPeriod(period, this.anchor(), step);
    // Back in the current period: follow today again.
    this.day.set(periodRange(period, day).end > this.today() ? null : day);
    this.load();
  }

  protected setLocation(locationId: string): void {
    this.locationId.set(locationId);
    this.load();
  }

  protected setNumber(text: string): void {
    this.numberText.set(text);
    clearTimeout(this.numberTimer);
    this.numberTimer = setTimeout(() => this.load(), 300);
  }

  protected clearFilters(): void {
    this.period.set('All');
    this.day.set(null);
    this.locationId.set('');
    this.numberText.set('');
    this.load();
  }

  protected showMore(): void {
    this.load(true);
  }

  protected next(order: OrderDto): OrderStatus | null {
    return NEXT[order.status];
  }

  protected move(order: OrderDto, status: OrderStatus, reason: string | null = null): void {
    this.busy.update(ids => new Set(ids).add(order.id));
    this.error.set(null);
    this.api.changeStatus(order.id, status, reason).subscribe({
      next: updated => {
        this.done(order.id);
        this.upsert(updated);
        this.rejecting.set(null);
        this.live.refreshCounts();
      },
      error: error => {
        this.done(order.id);
        this.error.set(errorMessage(error));
        this.load(); // someone else may have moved it already
      }
    });
  }

  protected openReject(order: OrderDto): void {
    this.rejectReason = this.t().rejectReasons[0]!;
    this.rejecting.set(order);
  }

  protected confirmReject(): void {
    const order = this.rejecting();
    if (order && this.rejectReason.trim()) this.move(order, 'Rejected', this.rejectReason.trim());
  }

  /** Table, Room…; a type the portal doesn't know is shown as the API names it. */
  protected locationType(order: OrderDto): string {
    return this.i18n.t().locationTypes[order.locationType] ?? order.locationType;
  }

  /** "Table 12" in the portal's language, for the table filter. */
  protected place(table: TableStatus): string {
    return `${this.i18n.t().locationTypes[table.locationType] ?? table.locationType} ${table.label}`;
  }

  protected statusLabel(order: OrderDto): string {
    return this.t().statuses[order.status] ?? order.status;
  }

  protected statusClass(status: OrderStatus): string {
    switch (status) {
      case 'Ready': return 'bg-accent-soft text-ink';
      case 'Completed': return 'bg-emerald-50 text-emerald-700';
      case 'Closed': return 'bg-surface text-ink-muted';
      case 'Rejected': return 'bg-red-50 text-red-800';
      default: return 'bg-amber-50 text-amber-800';
    }
  }

  protected money(order: OrderDto, minor: number): string {
    return formatMoney(minor, order.currency);
  }

  /** "3 min ago", then the time, and the date too for an order from another day. */
  protected age(order: OrderDto): string {
    const minutes = Math.max(0, Math.floor((this.now() - new Date(order.createdAt).getTime()) / 60_000));
    if (minutes < 1) return this.t().justNow;
    if (minutes < 60) return this.t().minutesAgo(minutes);
    const placed = new Date(order.createdAt);
    const time: Intl.DateTimeFormatOptions = { hour: '2-digit', minute: '2-digit' };
    return dayAt(placed, this.timeZone()) === this.today()
      ? placed.toLocaleTimeString(this.i18n.locale(), time)
      : placed.toLocaleString(this.i18n.locale(), { day: 'numeric', month: 'short', ...time });
  }

  /** Orders waiting more than 10 minutes in New are flagged. */
  protected isLate(order: OrderDto): boolean {
    return order.status === 'Submitted' && this.now() - new Date(order.createdAt).getTime() > 10 * 60_000;
  }

  protected isBusy(order: OrderDto): boolean {
    return this.busy().has(order.id);
  }

  /** Loads the first page for the current filters, or the next page after those on screen. */
  private load(append = false): void {
    const period = this.period();
    this.requests.next({
      append,
      query: {
        filter: this.filter(),
        period: period === 'All' ? undefined : period,
        date: period === 'All' ? undefined : this.anchor(),
        locationId: this.locationId() || undefined,
        number: this.number() ?? undefined,
        page: append ? this.page() + 1 : undefined
      }
    });
  }

  /** Puts a pushed or updated order in its place, dropping it if it no longer fits the filters. */
  private upsert(order: OrderDto): void {
    const fits = this.fitsFilter(order);
    const newestFirst = this.filter() === 'All';
    this.orders.update(list => {
      const others = (list ?? []).filter(o => o.id !== order.id);
      if (!fits) return others;
      return [...others, order].sort((a, b) => newestFirst ? b.number - a.number :
        Number(isDone(a)) - Number(isDone(b)) || (isDone(a) ? b.number - a.number : a.number - b.number));
    });
  }

  private fitsFilter(order: OrderDto): boolean {
    if (this.locationId() && order.serviceLocationId !== this.locationId()) return false;
    const number = this.number();
    if (number !== null && order.number !== number) return false;
    const period = this.period();
    if (period !== 'All') {
      const { start, end } = periodRange(period, this.anchor());
      const day = dayAt(order.createdAt, this.timeZone());
      if (day < start || day >= end) return false;
    }

    const filter = this.filter();
    switch (filter) {
      case 'All': return true;
      case 'Active': return COLUMN_OF[order.status] !== undefined;
      case 'Closed': return order.status === 'Closed';
      case 'Rejected': return order.status === 'Rejected';
      default: return COLUMN_OF[order.status] === filter;
    }
  }

  private highlight(id: string): void {
    this.fresh.update(ids => new Set(ids).add(id));
    setTimeout(() => this.fresh.update(ids => {
      const next = new Set(ids);
      next.delete(id);
      return next;
    }), 10_000);
  }

  private done(id: string): void {
    this.busy.update(ids => {
      const next = new Set(ids);
      next.delete(id);
      return next;
    });
  }
}
