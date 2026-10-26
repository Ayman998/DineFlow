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
  /** 1000 = 10% */
  serviceChargeBasisPoints: number;
  /** 500 = 5% */
  taxBasisPoints: number;
  pricesIncludeTax: boolean;
  /** Guests are offered a tip when paying. */
  tipsEnabled: boolean;
  /** One to four suggestions in basis points, e.g. [500, 1000, 1500]. */
  tipPresetsBasisPoints: number[];
  /** The suggestion shown as "Most common" and pre-selected; null for none. */
  mostCommonTipBasisPoints: number | null;
  /** Ask guests to rate their visit after paying. */
  reviewsEnabled: boolean;
  /** Where 4–5 star guests are invited to post publicly; null for nowhere. */
  reviewPlatform: ReviewPlatform | null;
  reviewUrl: string | null;
  /** Heading of the private feedback form for 1–3 stars; null for the default. */
  feedbackTitle: string | null;
}

export type ReviewPlatform = 'Google' | 'TripAdvisor' | 'Foursquare';

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

// ---- Orders (Sprint 3) ----

export type OrderStatus = 'Submitted' | 'Accepted' | 'Preparing' | 'Ready' | 'Completed' | 'Rejected';
export type OrderFilter = 'Active' | 'New' | 'Completed' | 'Rejected';

export interface OrderModifierDto {
  optionId: string;
  groupName: string;
  name: string;
  priceDeltaMinor: number;
}

export interface OrderDto {
  id: string;
  number: number;
  status: OrderStatus;
  venueId: string;
  serviceLocationId: string;
  locationType: string;
  locationLabel: string;
  areaName: string | null;
  currency: string;
  lines: {
    itemId: string;
    name: string;
    quantity: number;
    unitPriceMinor: number;
    lineTotalMinor: number;
    modifiers: OrderModifierDto[];
    note: string | null;
  }[];
  note: string | null;
  subtotalMinor: number;
  serviceChargeMinor: number;
  taxMinor: number;
  taxIncluded: boolean;
  totalMinor: number;
  rejectReason: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface OrderCounts {
  new: number;
  accepted: number;
  preparing: number;
  ready: number;
}

/** The standalone order queue (blueprint 9.2: /v1/merchant/orders/*). */
@Injectable({ providedIn: 'root' })
export class OrdersAdminApi {
  private readonly http = inject(HttpClient);

  list(venueId: string, filter: OrderFilter) {
    return this.http.get<OrderDto[]>(`/v1/merchant/venues/${venueId}/orders`, { params: { filter } });
  }

  counts(venueId: string) {
    return this.http.get<OrderCounts>(`/v1/merchant/venues/${venueId}/orders/counts`);
  }

  changeStatus(orderId: string, status: OrderStatus, reason: string | null = null) {
    return this.http.post<OrderDto>(`/v1/merchant/orders/${orderId}/status`, { status, reason });
  }
}

// ---- Payments and promotions (Sprint 4) ----

/** Pushed to staff screens when a guest's payment is confirmed by the provider. */
export interface PaymentNotice {
  tenantId: string;
  venueId: string;
  serviceLocationId: string;
  locationType: string;
  locationLabel: string;
  areaName: string | null;
  paymentId: string;
  reference: string;
  currency: string;
  amountMinor: number;
  tipMinor: number;
  remainingMinor: number;
  fullyPaid: boolean;
  /** How the guest split the bill: the rest of it, equal shares, their items or an amount. */
  mode: 'Full' | 'Equal' | 'Items' | 'Custom';
  shares: number | null;
  splitPeople: number | null;
  itemCount: number;
}

export type DiscountType = 'Percent' | 'Amount';

export interface PromoCode {
  id: string;
  code: string;
  headline: string;
  type: DiscountType;
  /** Percent in basis points (1000 = 10%) or a fixed amount in minor units. */
  value: number;
  maxDiscountMinor: number | null;
  minBillMinor: number | null;
  startsAt: string | null;
  endsAt: string | null;
  maxRedemptions: number | null;
  redemptionCount: number;
  isActive: boolean;
  /** Offered on the guest's payment confirmation for their next visit. */
  showAfterPayment: boolean;
}

export type PromoCodeRequest = Omit<PromoCode, 'id' | 'redemptionCount'>;

/** Promotion and voucher codes (blueprint 9.2 /v1/merchant/promotions). */
@Injectable({ providedIn: 'root' })
export class PromotionsApi {
  private readonly http = inject(HttpClient);

  list() {
    return this.http.get<PromoCode[]>('/v1/merchant/promotions');
  }

  create(request: PromoCodeRequest) {
    return this.http.post<PromoCode>('/v1/merchant/promotions', request);
  }

  update(id: string, request: PromoCodeRequest) {
    return this.http.put<PromoCode>(`/v1/merchant/promotions/${id}`, request);
  }
}

// ---- Reviews (Sprint 6) ----

export type ReviewKind = 'All' | 'Positive' | 'Constructive';
export type ReviewSort = 'Recent' | 'Oldest' | 'Highest' | 'Lowest';

export interface ReviewQuery {
  venueId: string;
  from?: string;
  to?: string;
  rating?: number | null;
  kind?: ReviewKind;
  withComment?: boolean;
  sort?: ReviewSort;
  page?: number;
  pageSize?: number;
}

export interface ReviewStats {
  count: number;
  average: number | null;
  /** Share of successful payments that left a rating (0–1). */
  ratedShare: number | null;
  /** Share of ratings whose guest opened the public review page (0–1). */
  publicShare: number | null;
}

export interface ReviewRow {
  id: string;
  createdAt: string;
  rating: number;
  comment: string | null;
  hasContact: boolean;
  locationType: string;
  locationLabel: string;
  staffName: string | null;
  publicLinkOpened: boolean;
  followedUp: boolean;
}

export interface ReviewPage {
  summary: { from: string; to: string; current: ReviewStats; perStar: number[]; previous: ReviewStats };
  items: ReviewRow[];
  total: number;
  page: number;
  pageSize: number;
}

export interface ReviewDetail {
  id: string;
  createdAt: string;
  rating: number;
  comment: string | null;
  positive: boolean;
  hasContact: boolean;
  publicPlatform: ReviewPlatform | null;
  publicLinkOpened: boolean;
  staffName: string | null;
  followedUpAt: string | null;
  followedUpBy: string | null;
  locationType: string;
  locationLabel: string;
  areaName: string | null;
  payment: {
    reference: string;
    paidAt: string | null;
    method: string;
    cardBrand: string | null;
    cardLast4: string | null;
    status: string;
    mode: string;
    shares: number | null;
    splitPeople: number | null;
    currency: string;
    billAmountMinor: number;
    discountMinor: number;
    tipMinor: number;
    amountMinor: number;
  };
  orders: { number: number; status: string; lines: { name: string; quantity: number; lineTotalMinor: number }[]; totalMinor: number }[];
}

export interface ReviewContact {
  name: string | null;
  email: string | null;
  phone: string | null;
  consentAt: string;
}

export interface StaffRating {
  staffUserId: string;
  name: string;
  count: number;
  average: number;
}

/** Pushed to staff screens when a guest rates their visit. */
export interface ReviewNotice {
  tenantId: string;
  venueId: string;
  reviewId: string;
  rating: number;
  locationType: string;
  locationLabel: string;
  hasComment: boolean;
}

/** The Reviews area (blueprint 9.2 /v1/merchant/reviews/*). */
@Injectable({ providedIn: 'root' })
export class ReviewsAdminApi {
  private readonly http = inject(HttpClient);

  list(query: ReviewQuery) {
    return this.http.get<ReviewPage>('/v1/merchant/reviews', { params: params(query) });
  }

  get(id: string) {
    return this.http.get<ReviewDetail>(`/v1/merchant/reviews/${id}`);
  }

  /** Owners and managers only; every view is audit-logged. */
  contact(id: string) {
    return this.http.get<ReviewContact>(`/v1/merchant/reviews/${id}/contact`);
  }

  followUp(id: string) {
    return this.http.post<ReviewDetail>(`/v1/merchant/reviews/${id}/follow-up`, {});
  }

  staffSummary(venueId: string, from?: string, to?: string) {
    return this.http.get<StaffRating[]>('/v1/merchant/reviews/staff-summary', { params: params({ venueId, from, to }) });
  }

  /** The filtered list as a CSV file. */
  export(query: ReviewQuery) {
    return this.http.get('/v1/merchant/reviews/export', { params: params(query), responseType: 'blob' });
  }
}

/** Query parameters without the empty ones. */
function params(values: object): Record<string, string> {
  const result: Record<string, string> = {};
  for (const [key, value] of Object.entries(values))
    if (value !== undefined && value !== null && value !== '') result[key] = String(value);
  return result;
}

// ---- Transactions, refunds, overview and live tables (Sprint 7) ----

export type TransactionState = 'Pending' | 'Succeeded' | 'PartiallyRefunded' | 'Refunded' | 'Failed' | 'Cancelled';
export type TransactionFilter = 'All' | 'Succeeded' | 'Refunded' | 'Pending' | 'Failed';
export type RefundStatus = 'Pending' | 'Succeeded' | 'Failed';

export interface TransactionQuery {
  venueId: string;
  from?: string;
  to?: string;
  status?: TransactionFilter;
  search?: string;
  page?: number;
  pageSize?: number;
}

export interface TransactionRow {
  id: string;
  reference: string;
  createdAt: string;
  succeededAt: string | null;
  state: TransactionState;
  method: string;
  cardBrand: string | null;
  cardLast4: string | null;
  mode: 'Full' | 'Equal' | 'Items' | 'Custom';
  /** Empty for a payment through a payment link. */
  locationType: string;
  locationLabel: string;
  /** The payment link it was paid through, e.g. PL-1A2B3C4D. */
  paymentLinkCode: string | null;
  orderNumbers: number[];
  currency: string;
  amountMinor: number;
  tipMinor: number;
  refundedMinor: number;
  refundPending: boolean;
  /** Where the payment stands with the venue's POS; null when it isn't posted to one. */
  posStatus: 'Pending' | 'Posted' | 'Failed' | null;
}

export interface TransactionPage {
  items: TransactionRow[];
  total: number;
  page: number;
  pageSize: number;
  totals: { count: number; grossMinor: number; tipsMinor: number; refundsMinor: number; netMinor: number };
}

export interface RefundRow {
  id: string;
  amountMinor: number;
  reason: string;
  status: RefundStatus;
  failureReason: string | null;
  requestedBy: string | null;
  createdAt: string;
  completedAt: string | null;
}

export interface TransactionDetail {
  summary: TransactionRow;
  areaName: string | null;
  paymentLinkId: string | null;
  paymentLinkReference: string | null;
  billAmountMinor: number;
  discountMinor: number;
  promoCode: string | null;
  tipMinor: number;
  amountMinor: number;
  refundableMinor: number;
  shares: number | null;
  splitPeople: number | null;
  itemCount: number;
  failureReason: string | null;
  reviewRating: number | null;
  reviewId: string | null;
  orders: { id: string; number: number; status: string; createdAt: string; lines: { name: string; quantity: number; lineTotalMinor: number }[]; totalMinor: number }[];
  refunds: RefundRow[];
  timeline: { at: string; kind: string; text: string }[];
}

export type OverviewPeriod = 'Today' | 'Week' | 'Month' | 'Quarter';
export type TrendGranularity = 'Hourly' | 'Daily' | 'Weekly' | 'Monthly';

export interface OverviewKpis {
  revenueMinor: number;
  tipsMinor: number;
  orders: number;
  refundsMinor: number;
  payments: number;
  paymentFailures: number;
}

export interface Overview {
  period: OverviewPeriod;
  from: string;
  to: string;
  currency: string;
  timeZone: string;
  selected: OverviewKpis;
  today: OverviewKpis;
  week: { revenueMinor: number; previousRevenueMinor: number; revenueChange: number | null; orders: number; previousOrders: number };
  granularity: TrendGranularity;
  trend: { start: string; revenueMinor: number; tipsMinor: number }[];
  attention: { kind: string; title: string; detail: string; count: number }[];
}

export type TableState = 'Available' | 'Ordering' | 'Unpaid' | 'PartiallyPaid' | 'Paid' | 'Attention';

export interface TableStatus {
  locationId: string;
  locationType: string;
  label: string;
  displayName: string;
  areaName: string | null;
  state: TableState;
  attentionReason: string | null;
  currency: string;
  totalMinor: number;
  paidMinor: number;
  tipsMinor: number;
  remainingMinor: number;
  lastActivityAt: string | null;
  checkId: string | null;
  orders: { id: string; number: number; status: string; createdAt: string; totalMinor: number }[];
}

/** Pushed to staff screens when the provider confirms or declines a refund. */
export interface RefundNotice {
  tenantId: string;
  venueId: string;
  paymentId: string;
  refundId: string;
  reference: string;
  currency: string;
  amountMinor: number;
  status: RefundStatus;
}

/** Transactions, refunds, the overview and the live table view (blueprint 9.2). */
@Injectable({ providedIn: 'root' })
export class FinanceApi {
  private readonly http = inject(HttpClient);

  transactions(query: TransactionQuery) {
    return this.http.get<TransactionPage>('/v1/merchant/transactions', { params: params(query) });
  }

  transaction(id: string) {
    return this.http.get<TransactionDetail>(`/v1/merchant/transactions/${id}`);
  }

  /** Owners and managers only; the refund completes when the provider confirms it. */
  refund(paymentId: string, clientRequestId: string, amountMinor: number, reason: string) {
    return this.http.post<TransactionDetail>(`/v1/merchant/transactions/${paymentId}/refunds`, { clientRequestId, amountMinor, reason });
  }

  overview(venueId: string, period: OverviewPeriod, granularity?: TrendGranularity) {
    return this.http.get<Overview>(`/v1/merchant/venues/${venueId}/overview`, { params: params({ period, granularity }) });
  }

  tableStatus(venueId: string) {
    return this.http.get<TableStatus[]>(`/v1/merchant/venues/${venueId}/table-status`);
  }
}


// ---- Payment links (Sprint 8) ----

export type PaymentLinkStatus = 'Active' | 'Paid' | 'Expired' | 'Cancelled';
export type PaymentLinkFilter = 'All' | PaymentLinkStatus;
export type ValidityUnit = 'Hours' | 'Days';

export interface PaymentLinkRow {
  id: string;
  code: string;
  reference: string | null;
  status: PaymentLinkStatus;
  currency: string;
  amountMinor: number;
  /** Paid towards the amount so far (tips not included). */
  paidMinor: number;
  tipsMinor: number;
  paymentCount: number;
  allowSplit: boolean;
  requestTip: boolean;
  customerEmail: string | null;
  createdAt: string;
  expiresAt: string;
  paidAt: string | null;
  /** The public link to share. */
  url: string;
}

export interface PaymentLinkPage {
  items: PaymentLinkRow[];
  total: number;
  page: number;
  pageSize: number;
  counts: { all: number; active: number; paid: number; expired: number; cancelled: number };
}

export interface PaymentLinkDetail {
  summary: PaymentLinkRow;
  remainingMinor: number;
  createdBy: string | null;
  customerNotifiedAt: string | null;
  cancelledAt: string | null;
  cancelledBy: string | null;
  payments: {
    id: string;
    reference: string;
    state: TransactionState;
    method: string;
    cardBrand: string | null;
    cardLast4: string | null;
    mode: 'Full' | 'Equal' | 'Items' | 'Custom';
    shares: number | null;
    splitPeople: number | null;
    billAmountMinor: number;
    tipMinor: number;
    amountMinor: number;
    refundedMinor: number;
    createdAt: string;
    succeededAt: string | null;
  }[];
  timeline: { at: string; kind: string; text: string }[];
}

export interface CreatePaymentLink {
  venueId: string;
  clientRequestId: string;
  amountMinor: number;
  reference: string | null;
  allowSplit: boolean;
  requestTip: boolean;
  validFor: number;
  validityUnit: ValidityUnit;
  customerEmail: string | null;
}

export interface PaymentLinkQuery {
  venueId: string;
  status?: PaymentLinkFilter;
  search?: string;
  page?: number;
  pageSize?: number;
}

/** Pushed to staff screens when a payment comes in through a payment link. */
export interface PaymentLinkNotice {
  tenantId: string;
  venueId: string;
  paymentLinkId: string;
  code: string;
  linkReference: string | null;
  paymentId: string;
  reference: string;
  currency: string;
  amountMinor: number;
  tipMinor: number;
  remainingMinor: number;
  fullyPaid: boolean;
}

/** Payment links (blueprint 9.2 /v1/merchant/payment-links/*). */
@Injectable({ providedIn: 'root' })
export class PaymentLinksApi {
  private readonly http = inject(HttpClient);

  list(query: PaymentLinkQuery) {
    return this.http.get<PaymentLinkPage>('/v1/merchant/payment-links', { params: params(query) });
  }

  get(id: string) {
    return this.http.get<PaymentLinkDetail>(`/v1/merchant/payment-links/${id}`);
  }

  /** Safe to retry with the same clientRequestId. */
  create(request: CreatePaymentLink) {
    return this.http.post<PaymentLinkDetail>('/v1/merchant/payment-links', request);
  }

  cancel(id: string) {
    return this.http.post<PaymentLinkDetail>(`/v1/merchant/payment-links/${id}/cancel`, {});
  }

  notify(id: string, email: string) {
    return this.http.post<PaymentLinkDetail>(`/v1/merchant/payment-links/${id}/notify`, { email });
  }

  export(query: PaymentLinkQuery) {
    return this.http.get('/v1/merchant/payment-links/export', { params: params(query), responseType: 'blob' });
  }
}

// ---- Notifications, reports, audit and feature flags (Sprint 9) ----

export type NotificationChannel = 'WhatsApp' | 'Email';
export type NotificationKind = 'PaymentAlert' | 'LinkPaymentAlert' | 'Report' | 'Test';
export type DeliveryStatus = 'Pending' | 'Sent' | 'Failed';

export interface AlertContact { id: string; name: string; phoneNumber: string; createdAt: string }

export interface AlertSettings {
  enabled: boolean;
  contacts: AlertContact[];
  /** "outbox" in development (messages are written to a folder), "meta" when the WhatsApp Cloud API is configured. */
  provider: string;
}

export interface Delivery {
  id: string;
  channel: NotificationChannel;
  kind: NotificationKind;
  recipient: string;
  subject: string | null;
  textBody: string;
  status: DeliveryStatus;
  attempts: number;
  createdAt: string;
  sentAt: string | null;
  nextAttemptAt: string | null;
  lastError: string | null;
  subjectId: string | null;
}

export interface DeliveryPage { items: Delivery[]; total: number; page: number; pageSize: number; failed: number }

export type ReportFrequency = 'Daily' | 'Weekly' | 'Monthly';

export interface ReportSchedule {
  id: string;
  frequency: ReportFrequency;
  sendAtHour: number;
  recipients: string[];
  isActive: boolean;
  nextRunAt: string;
  lastRunAt: string | null;
  createdBy: string | null;
}

export interface ReportScheduleRequest {
  venueId: string;
  frequency: ReportFrequency;
  sendAtHour: number;
  recipients: string[];
  isActive: boolean;
}

export interface ReportMetric { key: string; label: string; unit: 'Money' | 'Count' | 'Rating'; value: number; previous: number; change: number | null }

export interface Report {
  brandName: string;
  venueName: string;
  currency: string;
  frequency: ReportFrequency;
  periodStart: string;
  periodEnd: string;
  periodLabel: string;
  metrics: ReportMetric[];
  topItems: { name: string; quantity: number; totalMinor: number }[];
}

export interface AuditEntry { id: string; at: string; action: string; description: string; objectType: string; objectId: string; staffName: string | null }
export interface AuditPage { items: AuditEntry[]; total: number; page: number; pageSize: number }

export interface Features { paymentLinks: boolean; whatsAppAlerts: boolean; emailReports: boolean }

/** WhatsApp payment alerts and the message log (blueprint P1-STAFF-02). */
@Injectable({ providedIn: 'root' })
export class NotificationsApi {
  private readonly http = inject(HttpClient);

  alerts(venueId: string) {
    return this.http.get<AlertSettings>(`/v1/merchant/venues/${venueId}/alerts`);
  }

  setAlerts(venueId: string, enabled: boolean) {
    return this.http.put<AlertSettings>(`/v1/merchant/venues/${venueId}/alerts`, { enabled });
  }

  addContact(venueId: string, name: string, phoneNumber: string) {
    return this.http.post<AlertSettings>(`/v1/merchant/venues/${venueId}/alerts/contacts`, { name, phoneNumber });
  }

  removeContact(contactId: string) {
    return this.http.delete<AlertSettings>(`/v1/merchant/alerts/contacts/${contactId}`);
  }

  test(venueId: string) {
    return this.http.post<void>(`/v1/merchant/venues/${venueId}/alerts/test`, {});
  }

  deliveries(query: { venueId: string; channel?: NotificationChannel; status?: DeliveryStatus; page?: number; pageSize?: number }) {
    return this.http.get<DeliveryPage>('/v1/merchant/notifications', { params: params(query) });
  }

  resend(deliveryId: string) {
    return this.http.post<Delivery>(`/v1/merchant/notifications/${deliveryId}/resend`, {});
  }
}

/** Scheduled e-mail reports (blueprint 9.2 /v1/merchant/report-schedules/*). */
@Injectable({ providedIn: 'root' })
export class ReportsApi {
  private readonly http = inject(HttpClient);

  list(venueId: string) {
    return this.http.get<ReportSchedule[]>('/v1/merchant/report-schedules', { params: { venueId } });
  }

  create(request: ReportScheduleRequest) {
    return this.http.post<ReportSchedule>('/v1/merchant/report-schedules', request);
  }

  update(id: string, request: ReportScheduleRequest) {
    return this.http.put<ReportSchedule>(`/v1/merchant/report-schedules/${id}`, request);
  }

  delete(id: string) {
    return this.http.delete<void>(`/v1/merchant/report-schedules/${id}`);
  }

  sendNow(id: string) {
    return this.http.post<{ queued: number }>(`/v1/merchant/report-schedules/${id}/send`, {});
  }

  preview(venueId: string, frequency: ReportFrequency) {
    return this.http.get<Report>('/v1/merchant/report-schedules/preview', { params: { venueId, frequency } });
  }
}

/** The audit log and feature switches. */
@Injectable({ providedIn: 'root' })
export class AdminApi {
  private readonly http = inject(HttpClient);

  audit(query: { area?: string; page?: number; pageSize?: number }) {
    return this.http.get<AuditPage>('/v1/merchant/audit', { params: params(query) });
  }

  features() {
    return this.http.get<Features>('/v1/merchant/features');
  }
}

// ---- POS connector (Sprint 9b) ----

export type PosCommandKind = 'SendOrder' | 'PostPayment' | 'CloseCheck';
export type PosCommandStatus = 'Pending' | 'Done' | 'Failed';
export type SimulatorMode = 'Online' | 'Offline' | 'TimeoutAfterApplying' | 'Refuse';

export interface PosProvider { provider: string; displayName: string; capabilities: string[]; requiresAccessToken: boolean }

export interface PosConnection {
  provider: string;
  branchRef: string | null;
  hasAccessToken: boolean;
  isActive: boolean;
  autoClose: boolean;
  lastCheckedAt: string | null;
  lastCheckHealthy: boolean | null;
  lastCheckMessage: string | null;
}

export interface PosSettings {
  connection: PosConnection | null;
  providers: PosProvider[];
  tables: { locationId: string; displayName: string; areaName: string | null; externalRef: string | null }[];
  products: { itemId: string; name: string; category: string; posProductRef: string | null }[];
  /** Waiting more than 15 minutes, or refused by the POS. */
  waiting: number;
}

export interface PosConnectionRequest {
  provider: string;
  branchRef: string | null;
  /** null keeps the stored token; "" removes it. */
  accessToken: string | null;
  isActive: boolean;
  autoClose: boolean;
}

export interface PosCommand {
  id: string;
  kind: PosCommandKind;
  description: string;
  status: PosCommandStatus;
  needsAttention: boolean;
  attempts: number;
  createdAt: string;
  nextAttemptAt: string | null;
  completedAt: string | null;
  lastError: string | null;
  resultRef: string | null;
  paymentId: string | null;
}

export interface PosCommandPage { items: PosCommand[]; total: number; page: number; pageSize: number; waiting: number }

export interface PosSimulator {
  mode: SimulatorMode;
  checks: { checkRef: string; tableRef: string; totalMinor: number; paidMinor: number; isOpen: boolean;
    lines: { name: string; quantity: number; lineTotalMinor: number }[];
    payments: { postingRef: string; externalPaymentId: string | null; amountMinor: number; tipMinor: number }[] }[];
}

/** The venue's POS (blueprint E3, E9). */
@Injectable({ providedIn: 'root' })
export class PosApi {
  private readonly http = inject(HttpClient);

  settings(venueId: string) {
    return this.http.get<PosSettings>(`/v1/merchant/venues/${venueId}/pos`);
  }

  save(venueId: string, request: PosConnectionRequest) {
    return this.http.put<PosSettings>(`/v1/merchant/venues/${venueId}/pos`, request);
  }

  check(venueId: string) {
    return this.http.post<PosSettings>(`/v1/merchant/venues/${venueId}/pos/check`, {});
  }

  map(venueId: string, tables: { locationId: string; externalRef: string | null }[] | null, products: { itemId: string; posProductRef: string | null }[] | null) {
    return this.http.put<PosSettings>(`/v1/merchant/venues/${venueId}/pos/mappings`, { tables, products });
  }

  commands(venueId: string, status?: PosCommandStatus) {
    return this.http.get<PosCommandPage>('/v1/merchant/pos/commands', { params: params({ venueId, status, pageSize: 30 }) });
  }

  retry(commandId: string) {
    return this.http.post<PosCommand>(`/v1/merchant/pos/commands/${commandId}/retry`, {});
  }

  simulator(venueId: string) {
    return this.http.get<PosSimulator>(`/v1/merchant/venues/${venueId}/pos/simulator`);
  }

  setSimulator(venueId: string, mode: SimulatorMode) {
    return this.http.put<PosSimulator>(`/v1/merchant/venues/${venueId}/pos/simulator`, { mode });
  }
}

// ---- Settlements and reconciliation (Sprint 11) ----

export type DiscrepancyKind = 'NotSettled' | 'UnknownToDineFlow' | 'AmountMismatch' | 'RefundMismatch' | 'NotInPos' | 'SettledTwice';
export type DiscrepancyStatus = 'Open' | 'Cleared' | 'Resolved';

export interface Settlement {
  id: string;
  batchRef: string;
  provider: string;
  payoutDate: string;
  status: 'Expected' | 'Paid';
  source: 'File' | 'Provider';
  fileName: string | null;
  currency: string;
  transactions: number;
  grossMinor: number;
  refundsMinor: number;
  feesMinor: number;
  netMinor: number;
  importedAt: string;
}

export interface SettlementPage { items: Settlement[]; totals: { grossMinor: number; refundsMinor: number; feesMinor: number; netMinor: number } }

export interface SettlementDetail {
  settlement: Settlement;
  lines: { id: string; kind: 'Payment' | 'Refund' | 'Adjustment'; transactionDate: string; orderId: string | null; reference: string | null;
    amountMinor: number; feeMinor: number; paymentId: string | null; paymentReference: string | null }[];
}

export interface SettlementImportResult { settlements: Settlement[]; lines: number; days: string[]; openDiscrepancies: number }

export interface ReconciliationRun {
  businessDate: string;
  ranAt: string;
  currency: string;
  payments: number;
  ledgerGrossMinor: number;
  ledgerTipsMinor: number;
  ledgerRefundsMinor: number;
  settled: number;
  settledGrossMinor: number;
  settledFeesMinor: number;
  awaitingSettlement: number;
  postedToPos: number;
  matched: number;
  openDiscrepancies: number;
}

export interface Discrepancy {
  id: string;
  businessDate: string;
  kind: DiscrepancyKind;
  reference: string;
  expectedMinor: number;
  actualMinor: number;
  differenceMinor: number;
  details: string;
  status: DiscrepancyStatus;
  paymentId: string | null;
  createdAt: string;
  resolvedAt: string | null;
  resolvedBy: string | null;
  note: string | null;
}

export interface DiscrepancyPage { items: Discrepancy[]; total: number; page: number; pageSize: number; open: number }

/** Settlements, reconciliation and the ledger export (blueprint 9.2); owners and managers. */
@Injectable({ providedIn: 'root' })
export class ReconciliationApi {
  private readonly http = inject(HttpClient);

  settlements(venueId: string) {
    return this.http.get<SettlementPage>('/v1/merchant/settlements', { params: { venueId } });
  }

  settlement(id: string) {
    return this.http.get<SettlementDetail>(`/v1/merchant/settlements/${id}`);
  }

  importSettlement(venueId: string, file: File) {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<SettlementImportResult>('/v1/merchant/settlements/import', form, { params: { venueId } });
  }

  sandboxPayout(venueId: string) {
    return this.http.post<SettlementImportResult>('/v1/merchant/settlements/sandbox-payout', {}, { params: { venueId } });
  }

  runs(venueId: string) {
    return this.http.get<ReconciliationRun[]>('/v1/merchant/reconciliation', { params: { venueId } });
  }

  reconcile(venueId: string, day: string | null) {
    return this.http.post<ReconciliationRun>('/v1/merchant/reconciliation/run', { venueId, day });
  }

  discrepancies(venueId: string, status: DiscrepancyStatus | null) {
    return this.http.get<DiscrepancyPage>('/v1/merchant/reconciliation/discrepancies', { params: params({ venueId, status: status ?? '' }) });
  }

  resolve(id: string, note: string) {
    return this.http.post<Discrepancy>(`/v1/merchant/reconciliation/discrepancies/${id}/resolve`, { note });
  }

  ledger(venueId: string, from: string, to: string) {
    return this.http.get('/v1/merchant/ledger/export', { params: { venueId, from, to }, responseType: 'blob' });
  }
}
