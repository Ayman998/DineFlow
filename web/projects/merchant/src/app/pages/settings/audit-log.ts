import { Component, OnInit, inject, signal } from '@angular/core';

import { AdminApi, AuditPage } from '../../core/api';
import { errorMessage } from '../../core/auth';

const AREAS = [
  { value: '', label: 'Everything' },
  { value: 'payment', label: 'Payments, refunds and links' },
  { value: 'review', label: 'Guest contact details' },
  { value: 'alerts', label: 'WhatsApp alerts' },
  { value: 'report', label: 'E-mail reports' },
  { value: 'notification', label: 'Resent messages' },
  { value: 'settlement', label: 'Settlement imports' },
  { value: 'reconciliation', label: 'Resolved differences' },
  { value: 'pos', label: 'POS' }
];

/** Settings → Activity: the audit log for owners and managers (blueprint section 11, E15). */
@Component({
  selector: 'df-audit-log',
  template: `
    @let d = data();
    <section class="card max-w-4xl p-0">
      <div class="flex flex-wrap items-center justify-between gap-3 border-b border-line px-5 py-4">
        <div>
          <h2 class="font-semibold">Activity</h2>
          <p class="text-sm text-ink-muted">Sensitive actions, who took them and when. Entries can't be changed or deleted.</p>
        </div>
        <label class="flex items-center gap-2 text-sm text-ink-muted">Show
          <select class="field w-auto py-1" [value]="area()" (change)="setArea($any($event.target).value)">
            @for (a of areas; track a.value) { <option [value]="a.value">{{ a.label }}</option> }
          </select>
        </label>
      </div>
      @if (error()) { <p class="m-5 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-800" role="alert">{{ error() }}</p> }
      @if (d && !d.items.length) {
        <p class="px-5 py-10 text-center text-sm text-ink-muted">Nothing recorded yet.</p>
      } @else if (d) {
        <table class="w-full text-sm">
          <thead class="border-b border-line text-xs text-ink-muted uppercase">
            <tr>
              <th scope="col" class="px-5 py-2.5 text-start font-semibold">When</th>
              <th scope="col" class="px-3 py-2.5 text-start font-semibold">Who</th>
              <th scope="col" class="px-3 py-2.5 text-start font-semibold">What</th>
            </tr>
          </thead>
          <tbody>
            @for (e of d.items; track e.id) {
              <tr class="border-b border-line last:border-0">
                <td class="px-5 py-2.5 whitespace-nowrap text-ink-muted">{{ when(e.at) }}</td>
                <td class="px-3 py-2.5 whitespace-nowrap">{{ e.staffName ?? 'Someone' }}</td>
                <td class="px-3 py-2.5">{{ e.description }} <span class="text-xs text-ink-muted">({{ e.objectType }} {{ e.objectId.slice(0, 8) }})</span></td>
              </tr>
            }
          </tbody>
        </table>
        <div class="flex items-center justify-end gap-4 border-t border-line px-5 py-3 text-sm text-ink-muted">
          <span>{{ d.total ? (d.page - 1) * d.pageSize + 1 : 0 }}–{{ last(d) }} of {{ d.total }}</span>
          <span class="flex gap-1">
            <button type="button" class="btn-ghost px-2 py-1" aria-label="Previous page" [disabled]="d.page <= 1" (click)="goTo(d.page - 1)">‹</button>
            <button type="button" class="btn-ghost px-2 py-1" aria-label="Next page" [disabled]="last(d) >= d.total" (click)="goTo(d.page + 1)">›</button>
          </span>
        </div>
      } @else {
        <p class="px-5 py-6 text-sm text-ink-muted">Loading…</p>
      }
    </section>
  `
})
export class AuditLog implements OnInit {
  private readonly api = inject(AdminApi);

  protected readonly areas = AREAS;
  protected readonly area = signal('');
  protected readonly page = signal(1);
  protected readonly data = signal<AuditPage | null>(null);
  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.load();
  }

  protected setArea(area: string): void {
    this.area.set(area);
    this.page.set(1);
    this.load();
  }

  protected goTo(page: number): void {
    this.page.set(page);
    this.load();
  }

  protected last(d: AuditPage): number {
    return Math.min(d.total, d.page * d.pageSize);
  }

  protected when(iso: string): string {
    return new Date(iso).toLocaleString('en-GB', { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });
  }

  private load(): void {
    this.api.audit({ area: this.area() || undefined, page: this.page(), pageSize: 50 }).subscribe({
      next: page => {
        this.error.set(null);
        this.data.set(page);
      },
      error: error => this.error.set(errorMessage(error))
    });
  }
}
