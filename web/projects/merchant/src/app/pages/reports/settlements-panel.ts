import { Component, OnInit, inject, isDevMode, signal } from '@angular/core';

import { ReconciliationApi, Settlement, SettlementDetail, SettlementPage } from '../../core/api';
import { errorMessage } from '../../core/auth';
import { CurrentVenue } from '../../core/current-venue';
import { formatMoney } from '../../core/money';

/**
 * Reports → Settlements (blueprint 9.2): the acquirer's payout batches, imported from its
 * settlement report (CSV), each with its lines and what they matched in DineFlow.
 */
@Component({
  selector: 'df-settlements-panel',
  templateUrl: './settlements-panel.html'
})
export class SettlementsPanel implements OnInit {
  private readonly api = inject(ReconciliationApi);
  private readonly venue = inject(CurrentVenue);

  /** The sandbox payout only exists on a development API. */
  protected readonly devMode = isDevMode();

  protected readonly page = signal<SettlementPage | null>(null);
  protected readonly open = signal<SettlementDetail | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly showHelp = signal(false);

  ngOnInit(): void {
    this.venue.load().subscribe(() => this.load());
  }

  protected upload(input: HTMLInputElement): void {
    const file = input.files?.[0];
    const venueId = this.venue.venue()?.id;
    input.value = '';
    if (!file || !venueId || this.busy()) return;
    if (file.size > 5 * 1024 * 1024) return this.error.set('That file is over 5 MB. Export a shorter period.');
    this.run(this.api.importSettlement(venueId, file), `Imported ${file.name}`);
  }

  protected sandboxPayout(): void {
    const venueId = this.venue.venue()?.id;
    if (!venueId || this.busy()) return;
    this.run(this.api.sandboxPayout(venueId), 'Simulated a payout');
  }

  protected toggle(s: Settlement): void {
    if (this.open()?.settlement.id === s.id) return this.open.set(null);
    this.api.settlement(s.id).subscribe({ next: d => this.open.set(d), error: error => this.error.set(errorMessage(error)) });
  }

  protected money(minor: number, currency?: string): string {
    return formatMoney(minor, currency ?? this.venue.venue()?.currency ?? 'JOD');
  }

  protected day(iso: string): string {
    return new Date(`${iso}T12:00:00`).toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' });
  }

  private run(request: ReturnType<ReconciliationApi['sandboxPayout']>, what: string): void {
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    request.subscribe({
      next: result => {
        this.busy.set(false);
        const batches = result.settlements.map(s => s.batchRef).join(', ');
        const differences = result.openDiscrepancies
          ? `${result.openDiscrepancies} ${result.openDiscrepancies === 1 ? 'difference' : 'differences'} to look at on the Reconciliation tab.`
          : 'Everything matched.';
        this.notice.set(`${what}: ${batches}, ${result.lines} ${result.lines === 1 ? 'line' : 'lines'}. ${differences}`);
        this.load();
      },
      error: error => {
        this.busy.set(false);
        this.error.set(errorMessage(error));
      }
    });
  }

  private load(): void {
    const venueId = this.venue.venue()?.id;
    if (!venueId) return;
    this.api.settlements(venueId).subscribe({ next: p => this.page.set(p), error: error => this.error.set(errorMessage(error)) });
  }
}
