import { Component, OnInit, inject, isDevMode, signal } from '@angular/core';

import { ReconciliationApi, Settlement, SettlementDetail, SettlementPage } from '../../core/api';
import { errorMessage } from '../../core/auth';
import { CurrentVenue } from '../../core/current-venue';
import { I18n } from '../../core/i18n';
import { formatMoney } from '../../core/money';
import { SETTLEMENTS } from './reports.i18n';

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
  protected readonly i18n = inject(I18n);
  protected readonly t = this.i18n.use(SETTLEMENTS);

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
    if (file.size > 5 * 1024 * 1024) return this.error.set(this.t().fileTooLarge);
    this.run(this.api.importSettlement(venueId, file), this.t().imported(file.name));
  }

  protected sandboxPayout(): void {
    const venueId = this.venue.venue()?.id;
    if (!venueId || this.busy()) return;
    this.run(this.api.sandboxPayout(venueId), this.t().simulated);
  }

  protected toggle(s: Settlement): void {
    if (this.open()?.settlement.id === s.id) return this.open.set(null);
    this.api.settlement(s.id).subscribe({ next: d => this.open.set(d), error: error => this.error.set(errorMessage(error)) });
  }

  protected money(minor: number, currency?: string): string {
    return formatMoney(minor, currency ?? this.venue.venue()?.currency ?? 'JOD');
  }

  protected statusLabel(s: Settlement): string {
    return this.t().statuses[s.status] ?? s.status;
  }

  protected lineKind(kind: string): string {
    return this.t().lineKinds[kind] ?? kind;
  }

  protected day(iso: string): string {
    return new Date(`${iso}T12:00:00`).toLocaleDateString(this.i18n.locale(), { day: '2-digit', month: 'short', year: 'numeric' });
  }

  private run(request: ReturnType<ReconciliationApi['sandboxPayout']>, what: string): void {
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    request.subscribe({
      next: result => {
        this.busy.set(false);
        const batches = result.settlements.map(s => s.batchRef).join(', ');
        this.notice.set(this.t().result(what, batches, result.lines, result.openDiscrepancies));
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
