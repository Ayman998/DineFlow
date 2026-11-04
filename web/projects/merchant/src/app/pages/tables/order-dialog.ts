import { Component, DestroyRef, ElementRef, OnInit, afterNextRender, computed, inject, input, output, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { filter } from 'rxjs';

import { OrderDto, OrderModifierDto, OrderStatus, OrdersAdminApi } from '../../core/api';
import { errorMessage } from '../../core/auth';
import { I18n } from '../../core/i18n';
import { LiveOrders } from '../../core/live-orders';
import { formatMoney } from '../../core/money';
import { TABLES } from './tables.i18n';

type Line = OrderDto['lines'][number];

/**
 * One order from the live table view: what was ordered, with modifiers and notes, and its totals.
 * It follows the order live, so marking it ready or served on the Orders screen shows here too.
 */
@Component({
  selector: 'hz-order-dialog',
  template: `
    @let s = this.s();
    @let o = order();
    <div class="modal-backdrop" (click)="closed.emit()"></div>
    <div class="modal max-w-md" role="dialog" aria-modal="true" aria-labelledby="order-title" (keydown.escape)="closed.emit()">
      <div class="flex items-start justify-between gap-3">
        <div>
          <h2 id="order-title" class="text-lg font-bold">
            {{ s.title(number()) }}@if (round() > 1) { <span class="font-normal text-ink-muted"> · {{ strings().live.round(round()) }}</span> }
          </h2>
          @if (o) { <p class="text-sm text-ink-muted">{{ place(o) }} · {{ s.placed(time(o.createdAt)) }}</p> }
        </div>
        @if (o) {
          <span class="shrink-0 rounded-md px-2 py-0.5 text-xs font-bold" [class]="statusClass(o.status)" aria-live="polite">
            {{ statuses()[o.status] || o.status }}
          </span>
        }
      </div>

      @if (error()) {
        <p class="mt-4 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-800" role="alert">{{ error() }}</p>
      } @else if (!o) {
        <div class="mt-4 h-32 animate-pulse rounded-lg bg-surface" aria-busy="true"></div>
      } @else {
        <ul class="mt-4 divide-y divide-line rounded-lg border border-line text-sm">
          @for (line of o.lines; track $index) {
            <li class="flex gap-3 px-3 py-2">
              <span class="min-w-0 flex-1">
                <span class="font-bold">{{ line.quantity }}×</span> {{ name(line) }}
                @if (line.modifiers.length) { <span class="block text-xs text-ink-muted">{{ modifiers(line.modifiers) }}</span> }
                @if (line.note) { <span class="block text-xs font-semibold text-amber-900">“{{ line.note }}”</span> }
              </span>
              <span class="shrink-0 tabular-nums">{{ money(o, line.lineTotalMinor) }}</span>
            </li>
          }
        </ul>
        @if (o.note) { <p class="mt-2 rounded-md bg-amber-50 px-2 py-1 text-xs font-semibold text-amber-900">{{ s.note(o.note) }}</p> }
        @if (o.rejectReason) { <p class="mt-2 rounded-md bg-red-50 px-2 py-1 text-xs text-red-800">{{ s.rejected(o.rejectReason) }}</p> }

        <dl class="mt-3 grid grid-cols-[1fr_auto] gap-y-1 text-sm">
          <dt class="text-ink-muted">{{ s.subtotal }}</dt><dd class="text-end tabular-nums">{{ money(o, o.subtotalMinor) }}</dd>
          @if (o.serviceChargeMinor) {
            <dt class="text-ink-muted">{{ s.serviceCharge }}</dt><dd class="text-end tabular-nums">{{ money(o, o.serviceChargeMinor) }}</dd>
          }
          @if (o.taxMinor) {
            <dt class="text-ink-muted">{{ o.taxIncluded ? s.taxIncluded : s.tax }}</dt><dd class="text-end tabular-nums">{{ money(o, o.taxMinor) }}</dd>
          }
          <dt class="border-t border-line pt-1 font-semibold">{{ s.total }}</dt>
          <dd class="border-t border-line pt-1 text-end font-bold tabular-nums">{{ money(o, o.totalMinor) }}</dd>
        </dl>
      }

      <div class="mt-6 flex justify-end">
        <button #close type="button" class="btn-secondary" (click)="closed.emit()">{{ i18n.t().close }}</button>
      </div>
    </div>
  `
})
export class OrderDialog implements OnInit {
  private readonly api = inject(OrdersAdminApi);
  private readonly live = inject(LiveOrders);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly i18n = inject(I18n);
  protected readonly strings = this.i18n.use(TABLES);
  protected readonly s = computed(() => this.strings().order);
  protected readonly statuses = computed(() => this.strings().live.orderStatuses);

  readonly orderId = input.required<string>();
  /** Shown in the title while the order loads. */
  readonly number = input.required<number>();
  /** Which of the table's rounds it is; 1 for its first order. */
  readonly round = input(1);
  readonly closed = output<void>();

  protected readonly order = signal<OrderDto | null>(null);
  protected readonly error = signal<string | null>(null);
  private readonly closeButton = viewChild.required<ElementRef<HTMLButtonElement>>('close');

  constructor() {
    // Focus inside the dialog, so Escape closes it straight away.
    afterNextRender(() => this.closeButton().nativeElement.focus());
  }

  ngOnInit(): void {
    this.load();
    this.live.changed
      .pipe(filter(o => o.id === this.orderId()), takeUntilDestroyed(this.destroyRef))
      .subscribe(o => this.order.set(o));
    this.live.reconnected.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.load());
  }

  /** "Table 12 · Main hall" in the portal's language. */
  protected place(o: OrderDto): string {
    const place = `${this.i18n.t().locationTypes[o.locationType] ?? o.locationType} ${o.locationLabel}`;
    return o.areaName ? `${place} · ${o.areaName}` : place;
  }

  protected name(line: Line): string {
    return (this.i18n.lang() === 'ar' && line.nameAr) || line.name;
  }

  protected modifiers(modifiers: OrderModifierDto[]): string {
    const ar = this.i18n.lang() === 'ar';
    return modifiers.map(m => (ar && m.nameAr) || m.name).join(' · ');
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

  protected money(o: OrderDto, minor: number): string {
    return formatMoney(minor, o.currency);
  }

  protected time(iso: string): string {
    return new Date(iso).toLocaleTimeString(this.i18n.locale(), { hour: '2-digit', minute: '2-digit' });
  }

  private load(): void {
    this.api.get(this.orderId()).subscribe({
      next: o => {
        this.error.set(null);
        this.order.set(o);
      },
      error: error => this.error.set(errorMessage(error))
    });
  }
}
