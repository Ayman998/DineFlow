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

// ---- Menu (Sprint 2) ----

export type Allergen =
  | 'Gluten' | 'Crustaceans' | 'Eggs' | 'Fish' | 'Peanuts' | 'Soy' | 'Milk'
  | 'TreeNuts' | 'Celery' | 'Mustard' | 'Sesame' | 'Sulphites' | 'Lupin' | 'Molluscs';

export const ALLERGENS: { value: Allergen; label: string }[] = [
  { value: 'Gluten', label: 'Gluten' }, { value: 'Crustaceans', label: 'Crustaceans' }, { value: 'Eggs', label: 'Eggs' },
  { value: 'Fish', label: 'Fish' }, { value: 'Peanuts', label: 'Peanuts' }, { value: 'Soy', label: 'Soy' },
  { value: 'Milk', label: 'Milk / dairy' }, { value: 'TreeNuts', label: 'Tree nuts' }, { value: 'Celery', label: 'Celery' },
  { value: 'Mustard', label: 'Mustard' }, { value: 'Sesame', label: 'Sesame' }, { value: 'Sulphites', label: 'Sulphites' },
  { value: 'Lupin', label: 'Lupin' }, { value: 'Molluscs', label: 'Molluscs' }
];

export type Day = 'Sunday' | 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday';
export const DAYS: Day[] = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];

export interface ModifierOptionDto {
  id: string;
  name: string;
  nameAr: string | null;
  priceDeltaMinor: number;
  isDefault: boolean;
  isAvailable: boolean;
}

export interface ModifierGroupDto {
  id: string;
  name: string;
  nameAr: string | null;
  minSelect: number;
  maxSelect: number;
  options: ModifierOptionDto[];
}

export interface MenuItemDto {
  id: string;
  categoryId: string;
  name: string;
  nameAr: string | null;
  description: string | null;
  descriptionAr: string | null;
  priceMinor: number;
  imageUrl: string | null;
  isAvailable: boolean;
  allergens: Allergen[];
  tags: string[];
  sortOrder: number;
  modifierGroups: ModifierGroupDto[];
}

export interface CategoryDto {
  id: string;
  menuId: string;
  parentId: string | null;
  name: string;
  nameAr: string | null;
  sortOrder: number;
  items: MenuItemDto[];
  subcategories: CategoryDto[];
}

export interface MenuDto {
  id: string;
  name: string;
  nameAr: string | null;
  sortOrder: number;
  isPublished: boolean;
  days: Day[];
  startTime: string | null;
  endTime: string | null;
  availableNow: boolean;
  categories: CategoryDto[];
}

export interface MenuTreeDto {
  venueId: string;
  currency: string;
  menus: MenuDto[];
}

export interface MenuRequest {
  name: string;
  nameAr: string | null;
  isPublished: boolean;
  days: Day[];
  startTime: string | null;
  endTime: string | null;
}

export interface ItemRequest {
  categoryId: string;
  name: string;
  nameAr: string | null;
  description: string | null;
  descriptionAr: string | null;
  priceMinor: number;
  imageUrl: string | null;
  isAvailable: boolean;
  allergens: Allergen[];
  tags: string[];
  modifierGroups: {
    id: string | null;
    name: string;
    nameAr: string | null;
    minSelect: number;
    maxSelect: number;
    options: { id: string | null; name: string; nameAr: string | null; priceDeltaMinor: number; isDefault: boolean; isAvailable: boolean }[];
  }[];
}

/** Menu management endpoints (blueprint 9.2: /v1/merchant/menu/*). */
@Injectable({ providedIn: 'root' })
export class MenuAdminApi {
  private readonly http = inject(HttpClient);

  tree(venueId: string) {
    return this.http.get<MenuTreeDto>(`/v1/merchant/venues/${venueId}/menu`);
  }

  createMenu(venueId: string, request: MenuRequest) {
    return this.http.post<MenuDto>(`/v1/merchant/venues/${venueId}/menus`, request);
  }

  updateMenu(menuId: string, request: MenuRequest) {
    return this.http.put<MenuDto>(`/v1/merchant/menu/menus/${menuId}`, request);
  }

  deleteMenu(menuId: string) {
    return this.http.delete<void>(`/v1/merchant/menu/menus/${menuId}`);
  }

  createCategory(menuId: string, parentId: string | null, name: string, nameAr: string | null) {
    return this.http.post<CategoryDto>('/v1/merchant/menu/categories', { menuId, parentId, name, nameAr });
  }

  updateCategory(categoryId: string, name: string, nameAr: string | null) {
    return this.http.put<CategoryDto>(`/v1/merchant/menu/categories/${categoryId}`, { name, nameAr });
  }

  deleteCategory(categoryId: string) {
    return this.http.delete<void>(`/v1/merchant/menu/categories/${categoryId}`);
  }

  createItem(request: ItemRequest) {
    return this.http.post<MenuItemDto>('/v1/merchant/menu/items', request);
  }

  updateItem(itemId: string, request: ItemRequest) {
    return this.http.put<MenuItemDto>(`/v1/merchant/menu/items/${itemId}`, request);
  }

  setAvailability(itemId: string, isAvailable: boolean) {
    return this.http.put<MenuItemDto>(`/v1/merchant/menu/items/${itemId}/availability`, { isAvailable });
  }

  deleteItem(itemId: string) {
    return this.http.delete<void>(`/v1/merchant/menu/items/${itemId}`);
  }

  reorder(kind: 'menus' | 'categories' | 'items', ids: string[]) {
    return this.http.post<void>('/v1/merchant/menu/reorder', { kind, ids });
  }

  uploadImage(file: File) {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<{ url: string }>('/v1/merchant/menu/images', form);
  }
}
