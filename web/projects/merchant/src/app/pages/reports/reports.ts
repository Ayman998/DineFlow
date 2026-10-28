import { NgTemplateOutlet } from '@angular/common';
import { Component, OnInit, inject, signal, viewChild } from '@angular/core';

import { Report, ReportFrequency, ReportMetric, ReportSchedule, ReportsApi } from '../../core/api';
import { Auth, errorMessage } from '../../core/auth';
import { CurrentVenue } from '../../core/current-venue';
import { Features } from '../../core/features';
import { I18n } from '../../core/i18n';
import { formatMoney } from '../../core/money';
import { DeliveryLog } from '../../shared/delivery-log';
import { ReconciliationPanel } from './reconciliation-panel';
import { REPORTS } from './reports.i18n';
import { SettlementsPanel } from './settlements-panel';

/**
 * Reports: reconciliation and settlements for owners and managers (blueprint 9.2, Sprint 11),
 * and scheduled e-mail reports (blueprint 4.4, P1-MER-09): who gets the daily, weekly or monthly
 * report and when, a preview of the latest one, and what was e-mailed.
 */
@Component({
  selector: 'df-reports',
  imports: [DeliveryLog, NgTemplateOutlet, ReconciliationPanel, SettlementsPanel],
  templateUrl: './reports.html'
})
export class Reports implements OnInit {
  private readonly api = inject(ReportsApi);
  private readonly venue = inject(CurrentVenue);
  protected readonly auth = inject(Auth);
  protected readonly features = inject(Features);
  protected readonly i18n = inject(I18n);
  protected readonly t = this.i18n.use(REPORTS);
  private readonly log = viewChild(DeliveryLog);

  protected readonly tabs = ['reconciliation', 'settlements', 'email'] as const;
  /** Settlements and reconciliation are for owners and managers only. */
  protected readonly view = signal<'reconciliation' | 'settlements' | 'email'>(this.auth.canManage() ? 'reconciliation' : 'email');

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
    if (!recipients.length) return this.formError.set(this.t().recipientsRequired);

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
        this.notice.set(this.t().queued(result.queued));
        setTimeout(() => this.log()?.load(), 6_000);
      },
      error: error => {
        this.busy.set(false);
        this.error.set(errorMessage(error));
      }
    });
  }

  protected remove(s: ReportSchedule): void {
    if (!confirm(this.t().confirmStop(this.reportName(s.frequency), s.recipients.join(', ')))) return;
    this.api.delete(s.id).subscribe({ next: () => this.load(), error: error => this.error.set(errorMessage(error)) });
  }

  protected setPreview(frequency: ReportFrequency): void {
    this.previewFrequency.set(frequency);
    this.loadPreview();
  }

  protected describe(s: ReportSchedule): string {
    const at = `${String(s.sendAtHour).padStart(2, '0')}:00`;
    return this.t().describe[s.frequency]?.(at) ?? s.frequency;
  }

  protected frequencyLabel(frequency: ReportFrequency): string {
    return this.t().frequencies[frequency] ?? frequency;
  }

  protected reportName(frequency: ReportFrequency): string {
    return this.t().reportName[frequency] ?? `${frequency} report`;
  }

  /** The API's metric labels are English, so known metrics are labelled here by key. */
  protected metricLabel(metric: ReportMetric): string {
    return this.t().metrics[metric.key] ?? metric.label;
  }

  /** The API's period label is English ("Tue 21 Oct 2026"); in Arabic it's rebuilt from the dates. */
  protected periodLabel(report: Report): string {
    if (this.i18n.lang() === 'en') return report.periodLabel;
    const locale = this.i18n.locale();
    const date = (iso: string, options: Intl.DateTimeFormatOptions) =>
      new Date(`${iso}T00:00:00Z`).toLocaleDateString(locale, { ...options, timeZone: 'UTC' });
    switch (report.frequency) {
      case 'Monthly': return date(report.periodStart, { month: 'long', year: 'numeric' });
      case 'Weekly': {
        const last = new Date(`${report.periodEnd}T00:00:00Z`);
        last.setUTCDate(last.getUTCDate() - 1);
        const lastIso = last.toISOString().slice(0, 10);
        const sameYear = report.periodStart.slice(0, 4) === lastIso.slice(0, 4);
        return `${date(report.periodStart, { day: 'numeric', month: 'short', year: sameYear ? undefined : 'numeric' })} – ${date(lastIso, { day: 'numeric', month: 'short', year: 'numeric' })}`;
      }
      default: return date(report.periodStart, { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric' });
    }
  }

  protected value(report: Report, metric: ReportMetric, value: number): string {
    switch (metric.unit) {
      case 'Money': return formatMoney(value, report.currency);
      case 'Rating': return value ? `${value.toFixed(1)} / 5` : '–';
      default: return value.toLocaleString(this.i18n.locale());
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
      ? new Date(iso).toLocaleString(this.i18n.locale(), { weekday: 'short', day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit', timeZone: this.venue.venue()?.timeZone })
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
