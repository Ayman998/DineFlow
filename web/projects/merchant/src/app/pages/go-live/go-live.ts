import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { GoLive as GoLiveChecklist, GoLiveCheck, TeamApi } from '../../core/api';
import { errorMessage } from '../../core/auth';
import { CurrentVenue } from '../../core/current-venue';
import { I18n } from '../../core/i18n';
import { GO_LIVE } from './go-live.i18n';

/** Where to fix each check; null when it's done on the server or at the gateway. */
const LINKS: Record<string, string | null> = {
  'venue.tax': '/settings', 'venue.branding': '/settings', 'venue.reviews': '/settings',
  'tables.qr': '/tables', 'tables.scanned': '/tables',
  'menu.published': '/menu', 'menu.photos': '/items',
  'team.staff': '/settings',
  'payments.provider': null, 'payments.test_payment': '/tables', 'payments.webhook': null, 'payments.refund': '/transactions',
  'payments.receipt': '/transactions', 'payments.settlement': '/reports', 'payments.live': null,
  'alerts.whatsapp': '/settings', 'alerts.reports': '/reports', 'pos.connected': '/settings'
};

/** The pilot's go-live checklist for the venue (Sprint 12), worked out by the API from what has happened. */
@Component({
  selector: 'df-go-live',
  imports: [RouterLink],
  templateUrl: './go-live.html'
})
export class GoLive implements OnInit {
  private readonly api = inject(TeamApi);
  private readonly venue = inject(CurrentVenue);
  protected readonly i18n = inject(I18n);
  protected readonly t = this.i18n.use(GO_LIVE);

  protected readonly data = signal<GoLiveChecklist | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly loading = signal(false);

  /** Checks by group, in the order the API sends them. */
  protected readonly groups = computed(() => {
    const groups = new Map<string, GoLiveCheck[]>();
    for (const check of this.data()?.checks ?? []) groups.set(check.group, [...(groups.get(check.group) ?? []), check]);
    return [...groups.entries()].map(([key, checks]) => ({ key, checks }));
  });

  ngOnInit(): void {
    this.venue.load().subscribe(() => this.load());
  }

  protected load(): void {
    const venueId = this.venue.venue()?.id;
    if (!venueId) return;
    this.loading.set(true);
    this.api.goLive(venueId).subscribe({
      next: d => {
        this.loading.set(false);
        this.error.set(null);
        this.data.set(d);
      },
      error: error => {
        this.loading.set(false);
        this.error.set(errorMessage(error));
      }
    });
  }

  protected link(key: string): string | null {
    return LINKS[key] ?? null;
  }

  protected title(key: string): string {
    return this.t().checks[key]?.title ?? key;
  }

  protected hint(check: GoLiveCheck): string {
    return this.t().checks[check.key]?.hint(check.count ?? 0) ?? '';
  }
}
