import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { PaymentMethod, PaymentMode, PaymentStatus } from './payments';

export type PaymentLinkStatus = 'Active' | 'Paid' | 'Expired' | 'Cancelled';

/** A payment link as the customer sees it (blueprint 9.1 GET /v1/public/payment-links/{token}). */
export interface PaymentLink {
  code: string;
  reference: string | null;
  status: PaymentLinkStatus;
  venue: { brandName: string; branchName: string; logoUrl: string | null; brandColor: string; defaultLocale: string };
  currency: string;
  amountMinor: number;
  paidMinor: number;
  remainingMinor: number;
  /** What this customer can pay now: what's left minus payments others have in progress. */
  availableMinor: number;
  paymentInProgress: boolean;
  allowSplit: boolean;
  tips: { enabled: boolean; presetsBasisPoints: number[]; mostCommonBasisPoints: number | null };
  methods: PaymentMethod[];
  termsVersion: string;
  termsUrl: string;
  expiresAt: string;
}

export interface LinkPayment {
  id: string;
  reference: string;
  status: PaymentStatus;
  method: PaymentMethod;
  mode: PaymentMode;
  shares: number | null;
  splitPeople: number | null;
  currency: string;
  billAmountMinor: number;
  tipMinor: number;
  amountMinor: number;
  cardBrand: string | null;
  cardLast4: string | null;
  failureReason: string | null;
  /** The provider's payment page, while the customer still has to complete it there. */
  checkoutUrl: string | null;
  createdAt: string;
  succeededAt: string | null;
  remainingMinor: number;
  fullyPaid: boolean;
}

export interface CreateLinkPayment {
  clientRequestId: string;
  method: PaymentMethod;
  mode: 'Full' | 'Equal' | 'Custom';
  people: number | null;
  shares: number | null;
  amountMinor: number | null;
  tipMinor: number;
  /** The amount (before tip) the customer saw; nothing is charged if the server's differs. */
  expectedAmountMinor: number;
  acceptTerms: boolean;
}

/**
 * Payment-link endpoints. They're anonymous: having the link is what lets someone pay it, so the
 * guest session token is never sent (see guestAuthInterceptor).
 */
@Injectable({ providedIn: 'root' })
export class PaymentLinksApi {
  private readonly http = inject(HttpClient);

  get(token: string): Observable<PaymentLink> {
    return this.http.get<PaymentLink>(`/v1/public/payment-links/${encodeURIComponent(token)}`);
  }

  pay(token: string, request: CreateLinkPayment): Observable<LinkPayment> {
    return this.http.post<LinkPayment>(`/v1/public/payment-links/${encodeURIComponent(token)}/payments`, request);
  }

  payment(token: string, paymentId: string): Observable<LinkPayment> {
    return this.http.get<LinkPayment>(`/v1/public/payment-links/${encodeURIComponent(token)}/payments/${paymentId}`);
  }

  cancel(token: string, paymentId: string): Observable<LinkPayment> {
    return this.http.post<LinkPayment>(`/v1/public/payment-links/${encodeURIComponent(token)}/payments/${paymentId}/cancel`, {});
  }

  sendReceipt(token: string, paymentId: string, email: string): Observable<void> {
    return this.http.post<void>(`/v1/public/payment-links/${encodeURIComponent(token)}/payments/${paymentId}/receipt`, { email });
  }
}

/**
 * Paying <shares> of a link's amount split between <people>: each share rounds up so the shares
 * always cover it, capped at what's available. Mirrors the server's LinkTotals.EqualShare.
 */
export function linkEqualShare(link: PaymentLink, people: number, shares: number): number {
  if (people < 2 || shares < 1 || shares > people) return 0;
  return Math.min(link.availableMinor, Math.ceil(link.amountMinor / people) * shares);
}

/** sessionStorage key for the payment this customer last started on a link. */
export function pendingLinkPaymentKey(code: string): string {
  return `df.linkPayment.${code}`;
}
