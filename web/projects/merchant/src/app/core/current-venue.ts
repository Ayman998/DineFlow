import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, map, of, tap } from 'rxjs';

import { MerchantApi, Venue } from './api';
import { Auth } from './auth';

/**
 * The venue the dashboard is showing, and the tenant's other branches. Pages work on the first
 * branch; a branch switcher can set this once multi-branch tenants need one. Reloads when a
 * different tenant signs in.
 */
@Injectable({ providedIn: 'root' })
export class CurrentVenue {
  private readonly api = inject(MerchantApi);
  private readonly auth = inject(Auth);
  private readonly all = signal<Venue[]>([]);
  private loadedForTenant: string | null = null;

  readonly venue = signal<Venue | null>(null);
  /** All the tenant's branches in name order, with the current one as last saved. */
  readonly venues = computed(() => {
    const current = this.venue();
    return this.all().map(v => (v.id === current?.id ? current : v));
  });

  load(): Observable<Venue | null> {
    const tenantId = this.auth.profile()?.tenantId ?? null;
    const loaded = this.venue();
    if (loaded && this.loadedForTenant === tenantId) return of(loaded);

    return this.api.venues().pipe(
      tap(venues => {
        this.all.set(venues);
        this.venue.set(venues[0] ?? null);
        this.loadedForTenant = tenantId;
      }),
      map(venues => venues[0] ?? null)
    );
  }
}
