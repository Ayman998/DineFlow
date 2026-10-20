import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { minorDigits } from './menu';
import { OrderModifier, OrderStatus } from './orders';

export type PaymentMethod = 'Card' | 'ApplePay' | 'GooglePay' | 'StcPay';
export type PaymentStatus = 'Created' | 'RequiresAction' | 'Processing' | 'Succeeded' | 'Failed' | 'Cancelled';
export type BillStatus = 'NoOrders' | 'Open' | 'Paid';

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
  remainingMinor: number;
  /** Someone at the table has started paying and hasn't finished. */
  paymentInProgress: boolean;
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
  tipMinor: number;
  promoCode: string | null;
  expectedRemainingMinor: number;
  acceptTerms: boolean;
}

/** Bill and payment endpoints (blueprint 9.1). */
@Injectable({ providedIn: 'root' })
export class PaymentsApi {
  private readonly http = inject(HttpClient);

  bill(sessionId: string): Observable<Bill> {
    return this.http.get<Bill>(`/v1/public/sessions/${sessionId}/bill`);
  }

  validatePromo(sessionId: string, code: string): Observable<PromoQuote> {
    return this.http.post<PromoQuote>(`/v1/public/sessions/${sessionId}/promotions/validate`, { code });
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
