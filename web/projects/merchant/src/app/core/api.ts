import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';

export interface Venue {
  id: string;
  name: string;
  currency: string;
  timeZone: string;
  defaultLocale: 'en' | 'ar';
  logoUrl: string | null;
  brandColor: string;
  menuEnabled: boolean;
  payAtTableEnabled: boolean;
  isActive: boolean;
}

export type VenueSettings = Omit<Venue, 'id' | 'isActive'>;

export type LocationType = 'Table' | 'Counter' | 'HotelRoom' | 'PoolBeach';

export interface Qr {
  id: string;
  version: number;
  status: 'Active' | 'Disabled';
  url: string;
}

export interface Location {
  id: string;
  diningAreaId: string | null;
  type: LocationType;
  label: string;
  displayName: string;
  isActive: boolean;
  qr: Qr | null;
}

export interface Area {
  id: string;
  name: string;
  sortOrder: number;
  locations: Location[];
}

export interface FloorPlan {
  venueId: string;
  venueName: string;
  areas: Area[];
  unassigned: Location[];
}

/** Typed client for the merchant endpoints (blueprint 9.2). */
@Injectable({ providedIn: 'root' })
export class MerchantApi {
  private readonly http = inject(HttpClient);

  venues() {
    return this.http.get<Venue[]>('/v1/merchant/venues');
  }

  updateVenueSettings(venueId: string, settings: VenueSettings) {
    return this.http.put<Venue>(`/v1/merchant/venues/${venueId}/settings`, settings);
  }

  floorPlan(venueId: string) {
    return this.http.get<FloorPlan>(`/v1/merchant/venues/${venueId}/tables`);
  }

  createArea(venueId: string, name: string) {
    return this.http.post<Area>(`/v1/merchant/venues/${venueId}/areas`, { name });
  }

  createLocation(venueId: string, diningAreaId: string | null, type: LocationType, label: string) {
    return this.http.post<Location>('/v1/merchant/tables', { venueId, diningAreaId, type, label });
  }

  bulkCreateTables(venueId: string, diningAreaId: string | null, from: number, to: number) {
    return this.http.post<Location[]>('/v1/merchant/tables/bulk', { venueId, diningAreaId, from, to });
  }

  rotateQr(locationId: string) {
    return this.http.post<Location>(`/v1/merchant/tables/${locationId}/qr/rotate`, {});
  }

  setQrEnabled(locationId: string, enabled: boolean) {
    return this.http.post<Location>(`/v1/merchant/tables/${locationId}/qr/${enabled ? 'enable' : 'disable'}`, {});
  }
}
