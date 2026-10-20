import { Injectable, inject, signal } from '@angular/core';
import { Observable, map, of, tap } from 'rxjs';

import { MerchantApi, Venue } from './api';
import { Auth } from './auth';

/**
 * The venue the dashboard is showing. Tenants start with one venue; a branch switcher can
 * set this once multi-branch tenants exist. Reloads when a different tenant signs in.
 */
@Injectable({ providedIn: 'root' })
export class CurrentVenue {
  private readonly api = inject(MerchantApi);
  private readonly auth = inject(Auth);
  private loadedForTenant: string | null = null;

  readonly venue = signal<Venue | null>(null);

  load(): Observable<Venue | null> {
    const tenantId = this.auth.profile()?.tenantId ?? null;
    const loaded = this.venue();
    if (loaded && this.loadedForTenant === tenantId) return of(loaded);

    return this.api.venues().pipe(
      map(venues => venues[0] ?? null),
      tap(venue => {
        this.venue.set(venue);
        this.loadedForTenant = tenantId;
      })
    );
  }
}
