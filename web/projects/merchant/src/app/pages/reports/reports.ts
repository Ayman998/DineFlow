import { NgTemplateOutlet } from '@angular/common';
import { Component, OnInit, inject, signal, viewChild } from '@angular/core';

import { Report, ReportFrequency, ReportMetric, ReportSchedule, ReportsApi } from '../../core/api';
import { Auth, errorMessage } from '../../core/auth';
import { CurrentVenue } from '../../core/current-venue';
import { Features } from '../../core/features';
import { formatMoney } from '../../core/money';
import { DeliveryLog } from '../../shared/delivery-log';

/**
 * Reports (blueprint 4.4, P1-MER-09): scheduled e-mail reports (who gets the daily, weekly or
 * monthly report and when), a preview of the latest one, and what was e-mailed.
 */
@Component({
  selector: 'df-reports',
  imports: [DeliveryLog, NgTemplateOutlet],
  templateUrl: './reports.html'
})
export class Reports implements OnInit {
  private readonly api = inject(ReportsApi);
  private readonly venue = inject(CurrentVenue);
  protected readonly auth = inject(Auth);
  protected readonly features = inject(Features);
  private readonly log = viewChild(DeliveryLog);

  protected readonly frequencies: ReportFrequency[] = ['Daily', 'Weekly', 'Monthly'];
  protected readonly hours = Array.from({ length: 24 }, (_, h) => h);

  protected readonly schedules = signal<ReportSchedule[] | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly busy = signal(false);

  /** The schedule being edited; "new" while adding one. */
  protected readonly editing = signal<string | null>(null);
  protected readonly frequency = signal<ReportFrequency>('Daily');
  protected readonly hour = signal(8);
  protected readonly recipients = signal('');
  protected readonly active = signal(true);
  protected readonly formError = signal<string | null>(null);

  protected readonly previewFrequency = signal<ReportFrequency>('Daily');
  protected readonly preview = signal<Report | null>(null);

  ngOnInit(): void {
    this.venue.load().subscribe(() => {
      this.load();
      this.loadPreview();
    });
  }

  protected startNew(): void {
    this.editing.set('new');
    this.frequency.set('Daily');
    this.hour.set(8);
    this.recipients.set(this.auth.profile()?.email ?? '');
    this.active.set(true);
    this.formError.set(null);
  }

  protected edit(s: ReportSchedule): void {
    this.editing.set(s.id);
    this.frequency.set(s.frequency);
    this.hour.set(s.sendAtHour);
    this.recipients.set(s.recipients.join(', '));
    this.active.set(s.isActive);
    this.formError.set(null);
  }

  protected save(): void {
    const venueId = this.venue.venue()?.id;
    const editing = this.editing();
    if (!venueId || !editing || this.busy()) return;
    const recipients = this.recipients().split(/[\s,;]+/).map(r => r.trim()).filter(Boolean);
    if (!recipients.length) return this.formError.set('Add at least one e-mail address.');

    const request = { venueId, frequency: this.frequency(), sendAtHour: this.hour(), recipients, isActive: this.active() };
    this.busy.set(true);
    this.formError.set(null);
    (editing === 'new' ? this.api.create(request) : this.api.update(editing, request)).subscribe({
      next: () => {
        this.busy.set(false);
        this.editing.set(null);
        this.load();
      },
      error: error => {
        this.busy.set(false);
        this.formError.set(errorMessage(error));
      }
    });
  }

  protected sendNow(s: ReportSchedule): void {
    this.busy.set(true);
    this.notice.set(null);
    this.api.sendNow(s.id).subscribe({
      next: result => {
        this.busy.set(false);
        this.notice.set(`Queued for ${result.queued} ${result.queued === 1 ? 'recipient' : 'recipients'}. It goes out within a few seconds.`);
        setTimeout(() => this.log()?.load(), 6_000);
      },
      error: error => {
        this.busy.set(false);
        this.error.set(errorMessage(error));
      }
    });
  }

  protected remove(s: ReportSchedule): void {
    if (!confirm(`Stop the ${s.frequency.toLowerCase()} report to ${s.recipients.join(', ')}?`)) return;
    this.api.delete(s.id).subscribe({ next: () => this.load(), error: error => this.error.set(errorMessage(error)) });
  }

  protected setPreview(frequency: ReportFrequency): void {
    this.previewFrequency.set(frequency);
    this.loadPreview();
  }

  protected describe(s: ReportSchedule): string {
    const at = `${String(s.sendAtHour).padStart(2, '0')}:00`;
    return { Daily: `Every day at ${at}, for the day before`, Weekly: `Mondays at ${at}, for the week before`, Monthly: `On the 1st at ${at}, for the month before` }[s.frequency];
  }

  protected value(report: Report, metric: ReportMetric, value: number): string {
    switch (metric.unit) {
      case 'Money': return formatMoney(value, report.currency);
      case 'Rating': return value ? `${value.toFixed(1)} / 5` : '–';
      default: return value.toLocaleString('en-GB');
    }
  }

  protected change(metric: ReportMetric): string {
    if (metric.change === null) return '';
    if (metric.change === 0) return '=';
    return `${metric.change > 0 ? '▲' : '▼'} ${Math.abs(metric.change * 100).toFixed(1)}%`;
  }

  /** Green when it's good news: more revenue, fewer refunds or failures. */
  protected changeClass(metric: ReportMetric): string {
    if (!metric.change) return 'text-ink-muted';
    const up = metric.change > 0;
    const good = metric.key === 'refunds' || metric.key === 'failures' ? !up : up;
    return good ? 'text-emerald-700' : 'text-red-700';
  }

  protected money(minor: number, currency: string): string {
    return formatMoney(minor, currency);
  }

  /** In the venue's time zone, like the send hour, so "08:00" means the same everywhere. */
  protected when(iso: string | null): string {
    return iso
      ? new Date(iso).toLocaleString('en-GB', { weekday: 'short', day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit', timeZone: this.venue.venue()?.timeZone })
      : '–';
  }

  private load(): void {
    const venueId = this.venue.venue()?.id;
    if (!venueId) return;
    this.api.list(venueId).subscribe({
      next: list => {
        this.error.set(null);
        this.schedules.set(list);
      },
      error: error => this.error.set(errorMessage(error))
    });
  }

  private loadPreview(): void {
    const venueId = this.venue.venue()?.id;
    if (!venueId) return;
    this.api.preview(venueId, this.previewFrequency()).subscribe({ next: r => this.preview.set(r), error: () => this.preview.set(null) });
  }
}
