import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export type Allergen =
  | 'Gluten' | 'Crustaceans' | 'Eggs' | 'Fish' | 'Peanuts' | 'Soy' | 'Milk'
  | 'TreeNuts' | 'Celery' | 'Mustard' | 'Sesame' | 'Sulphites' | 'Lupin' | 'Molluscs';

export interface ModifierOption {
  id: string;
  name: string;
  nameAr: string | null;
  priceDeltaMinor: number;
  isDefault: boolean;
  isAvailable: boolean;
}

export interface ModifierGroup {
  id: string;
  name: string;
  nameAr: string | null;
  minSelect: number;
  maxSelect: number;
  options: ModifierOption[];
}

export interface MenuItem {
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
  modifierGroups: ModifierGroup[];
}

export interface MenuCategory {
  id: string;
  parentId: string | null;
  name: string;
  nameAr: string | null;
  items: MenuItem[];
  subcategories: MenuCategory[];
}

export interface Menu {
  id: string;
  name: string;
  nameAr: string | null;
  startTime: string | null;
  endTime: string | null;
  availableNow: boolean;
  categories: MenuCategory[];
}

export interface MenuTree {
  venueId: string;
  currency: string;
  menus: Menu[];
}

/** The live e-menu for the current table session. */
@Injectable({ providedIn: 'root' })
export class MenuApi {
  private readonly http = inject(HttpClient);

  load(sessionId: string): Observable<MenuTree> {
    return this.http.get<MenuTree>(`/v1/public/sessions/${sessionId}/menu`);
  }
}

/** Formats integer minor units in the venue currency, e.g. 2750 AED → "AED 27.50". */
export function formatMoney(minor: number, currency: string, lang: string): string {
  const digits = minorDigits(currency);
  return new Intl.NumberFormat(`${lang}-u-nu-latn`, {
    style: 'currency',
    currency,
    minimumFractionDigits: digits,
    maximumFractionDigits: digits
  }).format(minor / 10 ** digits);
}

/** Decimal places of a currency: 2 for AED, 3 for KWD/BHD/OMR/JOD. */
export function minorDigits(currency: string): number {
  return new Intl.NumberFormat('en', { style: 'currency', currency }).resolvedOptions().maximumFractionDigits ?? 2;
}
