import { Component, OnInit, inject, signal } from '@angular/core';

import { AdminApi, AuditPage } from '../../core/api';
import { errorMessage } from '../../core/auth';
import { I18n } from '../../core/i18n';
import { AUDIT_LOG } from './settings.i18n';

/** Area filters (action prefixes); labels are in the lexicon. */
const AREAS = ['', 'payment', 'review', 'alerts', 'report', 'notification', 'settlement', 'reconciliation', 'pos', 'staff', 'account'];

/** Settings → Activity: the audit log for owners and managers (blueprint section 11, E15). */
@Component({
  selector: 'df-audit-log',
  template: `
    @let d = data();
    @let t = this.t();
    <section class="card max-w-4xl p-0">
      <div class="flex flex-wrap items-center justify-between gap-3 border-b border-line px-5 py-4">
        <div>
          <h2 class="font-semibold">{{ t.heading }}</h2>
          <p class="text-sm text-ink-muted">{{ t.intro }}</p>
        </div>
        <label class="flex items-center gap-2 text-sm text-ink-muted">{{ t.show }}
          <select class="field w-auto py-1" [value]="area()" (change)="setArea($any($event.target).value)">
            @for (a of areas; track a) { <option [value]="a">{{ t.areas[a] ?? a }}</option> }
          </select>
        </label>
      </div>
      @if (error()) { <p class="m-5 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-800" role="alert">{{ error() }}</p> }
      @if (d && !d.items.length) {
        <p class="px-5 py-10 text-center text-sm text-ink-muted">{{ t.empty }}</p>
      } @else if (d) {
        <table class="w-full text-sm">
          <thead class="border-b border-line text-xs text-ink-muted uppercase">
            <tr>
              <th scope="col" class="px-5 py-2.5 text-start font-semibold">{{ t.when }}</th>
              <th scope="col" class="px-3 py-2.5 text-start font-semibold">{{ t.who }}</th>
              <th scope="col" class="px-3 py-2.5 text-start font-semibold">{{ t.what }}</th>
            </tr>
          </thead>
          <tbody>
            @for (e of d.items; track e.id) {
              <tr class="border-b border-line last:border-0">
                <td class="px-5 py-2.5 whitespace-nowrap text-ink-muted">{{ when(e.at) }}</td>
                <td class="px-3 py-2.5 whitespace-nowrap">{{ e.staffName ?? t.someone }}</td>
                <td class="px-3 py-2.5">{{ t.actions[e.action] ?? e.description }} <span class="text-xs text-ink-muted">({{ t.objectTypes[e.objectType] ?? e.objectType }} {{ e.objectId.slice(0, 8) }})</span></td>
              </tr>
            }
          </tbody>
        </table>
        <div class="flex items-center justify-end gap-4 border-t border-line px-5 py-3 text-sm text-ink-muted">
          <span>{{ t.range(d.total ? (d.page - 1) * d.pageSize + 1 : 0, last(d), d.total) }}</span>
          <span class="flex gap-1">
            <button type="button" class="btn-ghost px-2 py-1" [attr.aria-label]="t.previousPage" [disabled]="d.page <= 1" (click)="goTo(d.page - 1)">‹</button>
            <button type="button" class="btn-ghost px-2 py-1" [attr.aria-label]="t.nextPage" [disabled]="last(d) >= d.total" (click)="goTo(d.page + 1)">›</button>
          </span>
        </div>
      } @else {
        <p class="px-5 py-6 text-sm text-ink-muted">{{ i18n.t().loading }}</p>
      }
    </section>
  `
})
export class AuditLog implements OnInit {
  private readonly api = inject(AdminApi);
  protected readonly i18n = inject(I18n);
  protected readonly t = this.i18n.use(AUDIT_LOG);

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
    return new Date(iso).toLocaleString(this.i18n.locale(), { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });
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
