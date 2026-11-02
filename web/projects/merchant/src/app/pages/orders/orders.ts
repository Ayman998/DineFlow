import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';

import { OrderDto, OrderFilter, OrderStatus, OrdersAdminApi } from '../../core/api';
import { errorMessage } from '../../core/auth';
import { CurrentVenue } from '../../core/current-venue';
import { I18n } from '../../core/i18n';
import { LiveOrders } from '../../core/live-orders';
import { formatMoney } from '../../core/money';
import { ORDERS } from './orders.i18n';

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
  Rejected: null
};

/** Served and rejected orders are looked back on, newest first; the kitchen works oldest first. */
const isDone = (order: OrderDto) => order.status === 'Completed' || order.status === 'Rejected';

/**
 * The live order queue (blueprint 4.4 "Orders", P1-ORD-03/04): new orders arrive in real time
 * and staff move them New → Ready → Served, or reject them with a reason the guest sees. The
 * Served column shows what was served today.
 */
@Component({
  selector: 'hz-orders',
  imports: [FormsModule, NgTemplateOutlet],
  templateUrl: './orders.html'
})
export class Orders implements OnInit {
  private readonly api = inject(OrdersAdminApi);
  private readonly venue = inject(CurrentVenue);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly live = inject(LiveOrders);
  protected readonly i18n = inject(I18n);
  protected readonly t = this.i18n.use(ORDERS);

  protected readonly columns = COLUMNS;
  protected readonly filters: OrderFilter[] = ['Active', 'New', 'Ready', 'Served', 'Rejected'];
  protected readonly filter = signal<OrderFilter>('Active');
  protected readonly orders = signal<OrderDto[] | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal<ReadonlySet<string>>(new Set());
  protected readonly fresh = signal<ReadonlySet<string>>(new Set());
  protected readonly rejecting = signal<OrderDto | null>(null);
  protected rejectReason = '';

  /** Re-renders "x min ago" every 30 seconds. */
  protected readonly now = signal(Date.now());

  protected readonly byColumn = computed(() => {
    const groups = new Map<Column, OrderDto[]>();
    for (const order of this.orders() ?? []) {
      const column = COLUMN_OF[order.status];
      if (column) groups.set(column, [...(groups.get(column) ?? []), order]);
    }
    return groups;
  });

  ngOnInit(): void {
    this.venue.load().subscribe(() => this.load());

    this.live.placed.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(order => {
      if (this.filter() === 'Active' || this.filter() === 'New') {
        this.upsert(order);
        this.highlight(order.id);
      }
    });
    this.live.changed.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(order => this.upsert(order));
    this.live.reconnected.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.load());

    const timer = setInterval(() => this.now.set(Date.now()), 30_000);
    this.destroyRef.onDestroy(() => clearInterval(timer));
  }

  protected setFilter(filter: OrderFilter): void {
    this.filter.set(filter);
    this.load();
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

  protected money(order: OrderDto, minor: number): string {
    return formatMoney(minor, order.currency);
  }

  protected age(order: OrderDto): string {
    const minutes = Math.max(0, Math.floor((this.now() - new Date(order.createdAt).getTime()) / 60_000));
    if (minutes < 1) return this.t().justNow;
    if (minutes < 60) return this.t().minutesAgo(minutes);
    return new Date(order.createdAt).toLocaleTimeString(this.i18n.locale(), { hour: '2-digit', minute: '2-digit' });
  }

  /** Orders waiting more than 10 minutes in New are flagged. */
  protected isLate(order: OrderDto): boolean {
    return order.status === 'Submitted' && this.now() - new Date(order.createdAt).getTime() > 10 * 60_000;
  }

  protected isBusy(order: OrderDto): boolean {
    return this.busy().has(order.id);
  }

  private load(): void {
    const venueId = this.venue.venue()?.id;
    if (!venueId) return;
    this.api.list(venueId, this.filter()).subscribe({
      next: orders => this.orders.set(orders),
      error: error => this.error.set(errorMessage(error))
    });
  }

  /** Puts a pushed or updated order in its place, dropping it if it no longer fits the filter. */
  private upsert(order: OrderDto): void {
    const fits = this.fitsFilter(order);
    this.orders.update(list => {
      const others = (list ?? []).filter(o => o.id !== order.id);
      if (!fits) return others;
      return [...others, order].sort((a, b) =>
        Number(isDone(a)) - Number(isDone(b)) || (isDone(a) ? b.number - a.number : a.number - b.number));
    });
  }

  private fitsFilter(order: OrderDto): boolean {
    const filter = this.filter();
    switch (filter) {
      case 'Active': return COLUMN_OF[order.status] !== undefined;
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
