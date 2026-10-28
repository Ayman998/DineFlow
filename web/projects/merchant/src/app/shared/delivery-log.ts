import { Component, DestroyRef, OnInit, inject, input, signal } from '@angular/core';

import { Delivery, DeliveryPage, NotificationChannel, NotificationsApi } from '../core/api';
import { Auth, errorMessage } from '../core/auth';
import { CurrentVenue } from '../core/current-venue';
import { I18n } from '../core/i18n';
import { DELIVERY_LOG } from './delivery-log.i18n';

/**
 * What was sent on a channel and whether it arrived (blueprint "delivery failures are logged"):
 * newest first, with the provider's reason for anything that failed and a resend button for
 * managers. Refreshes every 15 seconds while it's on screen, since sending happens in the background.
 */
@Component({
  selector: 'df-delivery-log',
  template: `
    @let t = this.t();
    @let d = data();
    <div class="flex items-center justify-between gap-3">
      <h2 class="font-semibold">{{ title() ?? t.deliveryLog }}</h2>
      @if (d?.failed) { <span class="rounded-full bg-red-50 px-2.5 py-0.5 text-xs font-semibold text-red-800">{{ t.failedCount(d!.failed) }}</span> }
    </div>
    @if (error()) { <p class="mt-2 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-800" role="alert">{{ error() }}</p> }
    @if (d && !d.items.length) {
      <p class="mt-3 text-sm text-ink-muted">{{ t.nothingSent }}</p>
    } @else if (d) {
      <ul class="mt-3 flex flex-col divide-y divide-line">
        @for (m of d.items; track m.id) {
          <li class="flex items-start justify-between gap-3 py-2.5 text-sm">
            <span class="min-w-0">
              <span class="block font-medium">{{ kindLabel(m) }} · {{ m.recipient }}</span>
              <span class="block truncate text-xs text-ink-muted">{{ m.subject ?? firstLine(m.textBody) }}</span>
              <span class="block text-xs text-ink-muted">
                {{ when(m.sentAt ?? m.createdAt) }}@if (m.attempts > 1) { · {{ t.attempts(m.attempts) }} }
                @if (m.status === 'Pending' && m.attempts) { · {{ t.nextTry(when(m.nextAttemptAt)) }} }
              </span>
              @if (m.lastError && m.status !== 'Sent') { <span class="block text-xs text-red-700">{{ m.lastError }}</span> }
            </span>
            <span class="flex shrink-0 items-center gap-2">
              <span class="rounded-full px-2 py-0.5 text-xs font-semibold" [class]="statusClass(m)">{{ statusLabel(m) }}</span>
              @if (m.status === 'Failed' && auth.canManage()) {
                <button type="button" class="link text-xs" [disabled]="resending() === m.id" (click)="resend(m)">{{ t.resend }}</button>
              }
            </span>
          </li>
        }
      </ul>
      @if (d.total > d.items.length) { <p class="mt-2 text-xs text-ink-muted">{{ t.showing(d.items.length, d.total) }}</p> }
    } @else {
      <p class="mt-3 text-sm text-ink-muted">{{ i18n.t().loading }}</p>
    }
  `,
  host: { class: 'card block' }
})
export class DeliveryLog implements OnInit {
  private readonly api = inject(NotificationsApi);
  private readonly venue = inject(CurrentVenue);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly auth = inject(Auth);
  protected readonly i18n = inject(I18n);
  protected readonly t = this.i18n.use(DELIVERY_LOG);

  readonly channel = input.required<NotificationChannel>();
  /** Defaults to "Delivery log" in the current language. */
  readonly title = input<string>();

  protected readonly data = signal<DeliveryPage | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly resending = signal<string | null>(null);

  ngOnInit(): void {
    this.venue.load().subscribe(() => this.load());
    const timer = setInterval(() => this.load(), 15_000);
    this.destroyRef.onDestroy(() => clearInterval(timer));
  }

  /** Reloads now, e.g. right after something was queued. */
  load(): void {
    const venueId = this.venue.venue()?.id;
    if (!venueId) return;
    this.api.deliveries({ venueId, channel: this.channel(), pageSize: 20 }).subscribe({
      next: page => {
        this.error.set(null);
        this.data.set(page);
      },
      error: error => this.error.set(errorMessage(error))
    });
  }

  protected resend(message: Delivery): void {
    this.resending.set(message.id);
    this.api.resend(message.id).subscribe({
      next: () => {
        this.resending.set(null);
        this.load();
      },
      error: error => {
        this.resending.set(null);
        this.error.set(errorMessage(error));
      }
    });
  }

  protected kindLabel(m: Delivery): string {
    return this.t().kinds[m.kind] ?? m.kind;
  }

  protected statusLabel(m: Delivery): string {
    const status = m.status === 'Pending' ? (m.attempts ? 'Retrying' : 'Queued') : m.status;
    return this.t().statuses[status] ?? status;
  }

  protected statusClass(m: Delivery): string {
    switch (m.status) {
      case 'Sent': return 'bg-emerald-50 text-emerald-700';
      case 'Failed': return 'bg-red-50 text-red-800';
      default: return 'bg-amber-50 text-amber-800';
    }
  }

  protected firstLine(text: string): string {
    return text.split('\n').join(' · ');
  }

  protected when(iso: string | null): string {
    return iso ? new Date(iso).toLocaleString(this.i18n.locale(), { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' }) : '–';
  }
}
