import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';

import { OrderDto, OrderFilter, OrderStatus, OrdersAdminApi } from '../../core/api';
import { errorMessage } from '../../core/auth';
import { CurrentVenue } from '../../core/current-venue';
import { LiveOrders } from '../../core/live-orders';
import { formatMoney } from '../../core/money';

interface Column {
  status: OrderStatus;
  title: string;
}

interface Action {
  status: OrderStatus;
  label: string;
  primary: boolean;
}

const COLUMNS: Column[] = [
  { status: 'Submitted', title: 'New' },
  { status: 'Accepted', title: 'Accepted' },
  { status: 'Preparing', title: 'Preparing' },
  { status: 'Ready', title: 'Ready' }
];

const ACTIONS: Record<OrderStatus, Action[]> = {
  Submitted: [{ status: 'Accepted', label: 'Accept', primary: true }],
  Accepted: [{ status: 'Preparing', label: 'Start preparing', primary: true }, { status: 'Ready', label: 'Mark ready', primary: false }],
  Preparing: [{ status: 'Ready', label: 'Mark ready', primary: true }],
  Ready: [{ status: 'Completed', label: 'Mark served', primary: true }],
  Completed: [],
  Rejected: []
};

export const REJECT_REASONS = ['An item is sold out', 'The kitchen is closing', 'Duplicate order', 'Please order at the counter'];

/**
 * The live order queue (blueprint 4.4 "Orders", P1-ORD-03/04): new orders arrive in real time
 * and staff move them New → Accepted → Preparing → Ready → served, or reject them with a reason
 * the guest sees.
 */
@Component({
  selector: 'df-orders',
  imports: [FormsModule, NgTemplateOutlet],
  templateUrl: './orders.html'
})
export class Orders implements OnInit {
  private readonly api = inject(OrdersAdminApi);
  private readonly venue = inject(CurrentVenue);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly live = inject(LiveOrders);

  protected readonly columns = COLUMNS;
  protected readonly reasons = REJECT_REASONS;
  protected readonly filter = signal<OrderFilter>('Active');
  protected readonly orders = signal<OrderDto[] | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal<ReadonlySet<string>>(new Set());
  protected readonly fresh = signal<ReadonlySet<string>>(new Set());
  protected readonly rejecting = signal<OrderDto | null>(null);
  protected rejectReason = '';

  /** Re-renders "x min ago" every 30 seconds. */
  protected readonly now = signal(Date.now());

  protected readonly byStatus = computed(() => {
    const groups = new Map<OrderStatus, OrderDto[]>();
    for (const order of this.orders() ?? []) groups.set(order.status, [...(groups.get(order.status) ?? []), order]);
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

  protected actions(order: OrderDto): Action[] {
    return ACTIONS[order.status];
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
    this.rejectReason = REJECT_REASONS[0]!;
    this.rejecting.set(order);
  }

  protected confirmReject(): void {
    const order = this.rejecting();
    if (order && this.rejectReason.trim()) this.move(order, 'Rejected', this.rejectReason.trim());
  }

  protected money(order: OrderDto, minor: number): string {
    return formatMoney(minor, order.currency);
  }

  protected age(order: OrderDto): string {
    const minutes = Math.max(0, Math.floor((this.now() - new Date(order.createdAt).getTime()) / 60_000));
    if (minutes < 1) return 'just now';
    if (minutes < 60) return `${minutes} min ago`;
    return new Date(order.createdAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
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
      const merged = [...others, order];
      const newestFirst = this.filter() === 'Completed' || this.filter() === 'Rejected';
      return merged.sort((a, b) => (newestFirst ? b.number - a.number : a.number - b.number));
    });
  }

  private fitsFilter(order: OrderDto): boolean {
    switch (this.filter()) {
      case 'Active': return COLUMNS.some(c => c.status === order.status);
      case 'New': return order.status === 'Submitted';
      case 'Completed': return order.status === 'Completed';
      default: return order.status === 'Rejected';
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
