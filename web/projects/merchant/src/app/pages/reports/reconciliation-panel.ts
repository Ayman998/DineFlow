import { Component, OnInit, computed, inject, signal } from '@angular/core';

import { Discrepancy, DiscrepancyPage, DiscrepancyStatus, ReconciliationApi, ReconciliationRun } from '../../core/api';
import { errorMessage } from '../../core/auth';
import { CurrentVenue } from '../../core/current-venue';
import { formatMoney } from '../../core/money';

const KIND_LABELS: Record<string, string> = {
  NotSettled: 'Not settled',
  UnknownToDineFlow: 'Unknown to DineFlow',
  AmountMismatch: 'Amount differs',
  RefundMismatch: 'Refund differs',
  NotInPos: 'Not in POS',
  SettledTwice: 'Settled twice'
};

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
        this.notice.set(`Reconciled ${this.day(run.businessDate)}: ${run.openDiscrepancies ? run.openDiscrepancies + ' to look at' : 'everything adds up'}.`);
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
    if (note.length < 3) return this.error.set('Add a short note saying why this is acceptable.');
    this.api.resolve(d.id, note).subscribe({
      next: () => {
        this.resolving.set(null);
        this.notice.set(`Resolved ${d.reference}.`);
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
    return KIND_LABELS[kind] ?? kind;
  }

  protected money(minor: number): string {
    return formatMoney(minor, this.venue.venue()?.currency ?? 'JOD');
  }

  protected signed(minor: number): string {
    return (minor > 0 ? '+' : minor < 0 ? '−' : '') + this.money(Math.abs(minor));
  }

  protected day(iso: string): string {
    return new Date(`${iso}T12:00:00`).toLocaleDateString('en-GB', { weekday: 'short', day: '2-digit', month: 'short' });
  }

  private isoDay(offset: number): string {
    const d = new Date();
    d.setDate(d.getDate() + offset);
    return d.toISOString().slice(0, 10);
  }
}
