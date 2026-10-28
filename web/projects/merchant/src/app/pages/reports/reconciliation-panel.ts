import { Component, OnInit, computed, inject, signal } from '@angular/core';

import { Discrepancy, DiscrepancyPage, DiscrepancyStatus, ReconciliationApi, ReconciliationRun } from '../../core/api';
import { errorMessage } from '../../core/auth';
import { CurrentVenue } from '../../core/current-venue';
import { I18n } from '../../core/i18n';
import { formatMoney } from '../../core/money';
import { DifferenceValues, RECONCILIATION } from './reports.i18n';

/**
 * Reports → Reconciliation (blueprint 7.2, E12/E14): each day's ledger against the acquirer's
 * settlements and the POS, the queue of differences to work through, and the ledger export.
 */
@Component({
  selector: 'df-reconciliation-panel',
  templateUrl: './reconciliation-panel.html'
})
export class ReconciliationPanel implements OnInit {
  private readonly api = inject(ReconciliationApi);
  private readonly venue = inject(CurrentVenue);
  protected readonly i18n = inject(I18n);
  protected readonly t = this.i18n.use(RECONCILIATION);

  protected readonly runs = signal<ReconciliationRun[] | null>(null);
  protected readonly queue = signal<DiscrepancyPage | null>(null);
  protected readonly filter = signal<DiscrepancyStatus | null>('Open');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly resolving = signal<string | null>(null);
  protected readonly note = signal('');

  protected readonly exportFrom = signal(this.isoDay(-30));
  protected readonly exportTo = signal(this.isoDay(0));

  protected readonly totals = computed(() => {
    const runs = this.runs() ?? [];
    return {
      ledger: runs.reduce((sum, r) => sum + r.ledgerGrossMinor, 0),
      settled: runs.reduce((sum, r) => sum + r.settledGrossMinor, 0),
      fees: runs.reduce((sum, r) => sum + r.settledFeesMinor, 0),
      awaiting: runs.reduce((sum, r) => sum + r.awaitingSettlement, 0)
    };
  });

  ngOnInit(): void {
    this.venue.load().subscribe(() => this.load());
  }

  /** Reloads runs and the queue, e.g. after an import on the Settlements tab. */
  load(): void {
    const venueId = this.venue.venue()?.id;
    if (!venueId) return;
    this.api.runs(venueId).subscribe({ next: r => this.runs.set(r), error: error => this.error.set(errorMessage(error)) });
    this.api.discrepancies(venueId, this.filter()).subscribe({ next: q => this.queue.set(q), error: error => this.error.set(errorMessage(error)) });
  }

  protected setFilter(status: DiscrepancyStatus | null): void {
    this.filter.set(status);
    this.load();
  }

  protected reconcileYesterday(): void {
    const venueId = this.venue.venue()?.id;
    if (!venueId || this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    this.api.reconcile(venueId, null).subscribe({
      next: run => {
        this.busy.set(false);
        this.notice.set(this.t().reconciled(this.day(run.businessDate), run.openDiscrepancies));
        this.load();
      },
      error: error => {
        this.busy.set(false);
        this.error.set(errorMessage(error));
      }
    });
  }

  protected startResolve(d: Discrepancy): void {
    this.resolving.set(d.id);
    this.note.set('');
  }

  protected resolve(d: Discrepancy): void {
    const note = this.note().trim();
    if (note.length < 3) return this.error.set(this.t().noteRequired);
    this.api.resolve(d.id, note).subscribe({
      next: () => {
        this.resolving.set(null);
        this.notice.set(this.t().resolved(d.reference));
        this.load();
      },
      error: error => this.error.set(errorMessage(error))
    });
  }

  protected exportLedger(): void {
    const venueId = this.venue.venue()?.id;
    if (!venueId) return;
    this.api.ledger(venueId, this.exportFrom(), this.exportTo()).subscribe({
      next: blob => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `ledger-${this.exportFrom()}-${this.exportTo()}.csv`;
        a.click();
        URL.revokeObjectURL(url);
      },
      error: error => this.error.set(errorMessage(error))
    });
  }

  protected kindLabel(kind: string): string {
    return this.t().kinds[kind] ?? kind;
  }

  protected statusLabel(status: DiscrepancyStatus): string {
    return this.t().statuses[status] ?? status;
  }

  protected money(minor: number): string {
    return formatMoney(minor, this.venue.venue()?.currency ?? 'JOD');
  }

  /** What a difference is about, in the portal's language; a code the portal doesn't know keeps the API's English. */
  protected details(d: Discrepancy): string {
    const words = this.t().details as Record<string, ((values: DifferenceValues) => string) | undefined>;
    const word = d.detailsCode ? words[d.detailsCode] : undefined;
    if (!word) return d.details;
    const p = d.detailsParams ?? {};
    return word({
      reference: d.reference,
      expected: this.money(d.expectedMinor),
      actual: this.money(d.actualMinor),
      day: this.day(d.businessDate),
      count: Number(p['count'] ?? 0),
      attempts: Number(p['attempts'] ?? 0),
      error: typeof p['error'] === 'string' ? p['error'] : null
    });
  }

  protected signed(minor: number): string {
    return (minor > 0 ? '+' : minor < 0 ? '−' : '') + this.money(Math.abs(minor));
  }

  protected day(iso: string): string {
    return new Date(`${iso}T12:00:00`).toLocaleDateString(this.i18n.locale(), { weekday: 'short', day: '2-digit', month: 'short' });
  }

  private isoDay(offset: number): string {
    const d = new Date();
    d.setDate(d.getDate() + offset);
    return d.toISOString().slice(0, 10);
  }
}
