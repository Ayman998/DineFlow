import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { minorDigits } from './menu';
import { OrderModifier, OrderStatus } from './orders';

export type PaymentMethod = 'Card' | 'ApplePay' | 'GooglePay' | 'StcPay';
export type PaymentStatus = 'Created' | 'RequiresAction' | 'Processing' | 'Succeeded' | 'Failed' | 'Cancelled';
export type BillStatus = 'NoOrders' | 'Open' | 'Paid';
/** The split-payment selector (blueprint 4.3): the rest of the bill, equal shares, items or an amount. */
export type PaymentMode = 'Full' | 'Equal' | 'Items' | 'Custom';
export type BillUnitStatus = 'Open' | 'Selected' | 'Paying' | 'Paid';

/** One portion of an order line, e.g. the first of two Cokes, priced with its share of service and VAT. */
export interface BillUnit {
  orderLineId: string;
  unitIndex: number;
  orderNumber: number;
  name: string;
  nameAr: string | null;
  options: string[];
  amountMinor: number;
  status: BillUnitStatus;
  /** Held, being paid or paid by this guest. */
  mine: boolean;
}

export interface BillSplit {
  /** People the table splits between equally; null when not splitting. */
  people: number | null;
  sharesTaken: number;
  sharesLeft: number | null;
  equalAvailable: boolean;
  itemsAvailable: boolean;
  /** The items this guest holds. */
  selectedMinor: number;
}

export interface UnitRef {
  orderLineId: string;
  unitIndex: number;
}

/** What the guest has chosen to pay. */
export type Selection =
  | { mode: 'Full' }
  | { mode: 'Equal'; shares: number }
  | { mode: 'Items' }
  | { mode: 'Custom'; amountMinor: number };

export interface BillOrder {
  id: string;
  number: number;
  status: OrderStatus;
  lines: {
    name: string;
    nameAr: string | null;
    quantity: number;
    lineTotalMinor: number;
    modifiers: OrderModifier[];
  }[];
  totalMinor: number;
}

/** The table's bill (blueprint 9.1 GET …/bill): what's been ordered, paid and is left. */
export interface Bill {
  checkId: string | null;
  status: BillStatus;
  currency: string;
  locationType: string;
  locationLabel: string;
  areaName: string | null;
  orders: BillOrder[];
  subtotalMinor: number;
  serviceChargeMinor: number;
  taxMinor: number;
  taxIncluded: boolean;
  totalMinor: number;
  paidMinor: number;
  /** Left to pay on the bill. */
  remainingMinor: number;
  /** Part of what's left that others are paying or have selected right now. */
  reservedMinor: number;
  /** What this guest can pay now. */
  availableMinor: number;
  /** Someone at the table has started paying and hasn't finished. */
  paymentInProgress: boolean;
  split: BillSplit;
  units: BillUnit[];
  tips: { enabled: boolean; presetsBasisPoints: number[]; mostCommonBasisPoints: number | null };
  methods: PaymentMethod[];
  termsVersion: string;
  termsUrl: string;
}

export interface PromoQuote {
  code: string;
  headline: string;
  discountMinor: number;
}

export interface Payment {
  id: string;
  reference: string;
  status: PaymentStatus;
  method: PaymentMethod;
  mode: PaymentMode;
  shares: number | null;
  splitPeople: number | null;
  itemCount: number;
  currency: string;
  billAmountMinor: number;
  discountMinor: number;
  tipMinor: number;
  amountMinor: number;
  promoCode: string | null;
  cardBrand: string | null;
  cardLast4: string | null;
  failureReason: string | null;
  /** The provider's payment page, while the guest still has to complete it there. */
  checkoutUrl: string | null;
  locationType: string;
  locationLabel: string;
  areaName: string | null;
  createdAt: string;
  succeededAt: string | null;
  remainingMinor: number | null;
  fullyPaid: boolean | null;
  nextVisitOffer: { code: string; headline: string } | null;
}

export interface CreatePayment {
  sessionId: string;
  clientRequestId: string;
  method: PaymentMethod;
  mode: PaymentMode;
  shares: number | null;
  amountMinor: number | null;
  tipMinor: number;
  promoCode: string | null;
  /** The guest's share of the bill as shown; nothing is charged if the server's differs. */
  expectedAmountMinor: number;
  acceptTerms: boolean;
}

export interface ReserveResult {
  bill: Bill;
  /** Requested items another guest already has. */
  taken: UnitRef[];
}

/** Bill and payment endpoints (blueprint 9.1). */
@Injectable({ providedIn: 'root' })
export class PaymentsApi {
  private readonly http = inject(HttpClient);

  bill(sessionId: string): Observable<Bill> {
    return this.http.get<Bill>(`/v1/public/sessions/${sessionId}/bill`);
  }

  /** Checks a code against what the guest is about to pay. */
  validatePromo(sessionId: string, code: string, amountMinor: number | null): Observable<PromoQuote> {
    return this.http.post<PromoQuote>(`/v1/public/sessions/${sessionId}/promotions/validate`, { code, amountMinor });
  }

  /** Sets how many people the table splits equally between; null removes the split. */
  setSplit(sessionId: string, people: number | null): Observable<Bill> {
    return this.http.put<Bill>(`/v1/public/sessions/${sessionId}/split`, { people });
  }

  /** Holds exactly these items for this guest; anything left out is let go. */
  reserve(sessionId: string, units: UnitRef[]): Observable<ReserveResult> {
    return this.http.post<ReserveResult>(`/v1/public/sessions/${sessionId}/allocations/reserve`, { units });
  }

  create(request: CreatePayment): Observable<Payment> {
    return this.http.post<Payment>('/v1/public/payments', request);
  }

  get(paymentId: string): Observable<Payment> {
    return this.http.get<Payment>(`/v1/public/payments/${paymentId}`);
  }

  cancel(paymentId: string): Observable<Payment> {
    return this.http.post<Payment>(`/v1/public/payments/${paymentId}/cancel`, {});
  }

  sendReceipt(paymentId: string, email: string): Observable<void> {
    return this.http.post<void>(`/v1/public/payments/${paymentId}/receipt`, { email });
  }
}

/** sessionStorage key for the payment this guest last started, so they can continue or cancel it. */
export const PENDING_PAYMENT_KEY = 'df.pendingPayment';

/** Display order of payment methods, from the Checkout mockup. */
export const METHOD_ORDER: PaymentMethod[] = ['ApplePay', 'GooglePay', 'Card', 'StcPay'];

export function isFinal(status: PaymentStatus): boolean {
  return status === 'Succeeded' || status === 'Failed' || status === 'Cancelled';
}

/** Only follow http(s) links from the API, never javascript: or data: URLs. */
export function safeUrl(url: string | null | undefined): string | null {
  if (!url) return null;
  try {
    const parsed = new URL(url, window.location.origin);
    return parsed.protocol === 'https:' || parsed.protocol === 'http:' ? parsed.href : null;
  } catch {
    return null;
  }
}

export type CustomTip = { minor: number } | { error: 'invalid' | 'tooHigh' } | null;

export function percentOf(minor: number, basisPoints: number): number {
  return Math.round((minor * basisPoints) / 10_000);
}

/** "12%" is a percentage of the bill; "10" or "7.50" is an amount in the venue's currency. */
export function parseTip(text: string, billMinor: number, currency: string): CustomTip {
  const value = text.trim().replace(/[٠-٩]/g, d => String(d.charCodeAt(0) - 0x0660)).replace('٫', '.').replace(',', '.');
  if (!value) return null;

  const percent = value.endsWith('%') || value.endsWith('٪');
  const number = percent ? value.slice(0, -1).trim() : value;
  if (!/^\d+(\.\d+)?$/.test(number)) return { error: 'invalid' };

  const minor = percent
    ? Math.round((billMinor * Number(number)) / 100)
    : Math.round(Number(number) * 10 ** minorDigits(currency));
  if (minor > billMinor) return { error: 'tooHigh' };
  return { minor };
}

/**
 * Paying <shares> of the <sharesLeft> equal shares, out of what's available: shares round down
 * and the last one pays exactly what's left. Mirrors the server's BillAllocation.EqualShare.
 */
export function equalShare(availableMinor: number, sharesLeft: number, shares: number): number {
  if (sharesLeft < 1 || shares < 1 || shares > sharesLeft || availableMinor <= 0) return 0;
  return shares === sharesLeft ? availableMinor : Math.floor((availableMinor * shares) / sharesLeft);
}

/** The share of the bill (before tip and discount) a selection pays, as the server will work it out. */
export function principalOf(bill: Bill, selection: Selection): number {
  switch (selection.mode) {
    case 'Full': return bill.availableMinor;
    case 'Equal': return equalShare(bill.availableMinor, bill.split.sharesLeft ?? 0, selection.shares);
    case 'Items': return bill.split.selectedMinor;
    case 'Custom': return Math.min(selection.amountMinor, bill.availableMinor);
  }
}

export function sameUnit(a: UnitRef, b: UnitRef): boolean {
  return a.orderLineId === b.orderLineId && a.unitIndex === b.unitIndex;
}

/** "25" or "25.50" in the venue's currency, accepting Arabic digits; null when empty. */
export function parseAmount(text: string, currency: string): { minor: number } | { error: 'invalid' } | null {
  const value = text.trim().replace(/[٠-٩]/g, d => String(d.charCodeAt(0) - 0x0660)).replace('٫', '.').replace(',', '.');
  if (!value) return null;
  const digits = minorDigits(currency);
  const [whole, fraction, extra] = value.split('.');
  if (extra !== undefined || !/^\d+$/.test(whole ?? '') || (fraction !== undefined && !new RegExp(`^[0-9]{1,${digits}}$`).test(fraction)))
    return { error: 'invalid' };
  return { minor: Math.round(Number(value) * 10 ** digits) };
}
