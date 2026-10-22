import { Injectable, inject, signal } from '@angular/core';

import { AdminApi, Features as FeatureFlags } from './api';

/**
 * Which switchable features the API has on (blueprint E15 "feature flags"). Everything counts as
 * on until the API says otherwise, so a slow or failed check never hides working features.
 */
@Injectable({ providedIn: 'root' })
export class Features {
  private readonly api = inject(AdminApi);

  readonly current = signal<FeatureFlags>({ paymentLinks: true, whatsAppAlerts: true, emailReports: true });

  load(): void {
    this.api.features().subscribe({ next: flags => this.current.set(flags), error: () => undefined });
  }
}
