import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export type ReviewPlatform = 'Google' | 'TripAdvisor' | 'Foursquare';

export interface ReviewResult {
  id: string;
  rating: number;
  positive: boolean;
  /** For 4–5 stars when the venue has a public review page. */
  publicReview: { platform: ReviewPlatform; url: string } | null;
  /** For 1–3 stars: the heading of the private feedback form. */
  feedbackTitle: string | null;
  feedbackSent: boolean;
}

export interface Feedback {
  comment: string | null;
  name: string | null;
  email: string | null;
  phone: string | null;
  consent: boolean;
}

/** Rating the visit after paying (blueprint 9.1 POST …/reviews). */
@Injectable({ providedIn: 'root' })
export class ReviewsApi {
  private readonly http = inject(HttpClient);

  submit(sessionId: string, paymentId: string, rating: number, comment: string | null): Observable<ReviewResult> {
    return this.http.post<ReviewResult>(`/v1/public/sessions/${sessionId}/reviews`, { paymentId, rating, comment });
  }

  feedback(sessionId: string, reviewId: string, feedback: Feedback): Observable<ReviewResult> {
    return this.http.put<ReviewResult>(`/v1/public/sessions/${sessionId}/reviews/${reviewId}/feedback`, feedback);
  }

  publicOpened(sessionId: string, reviewId: string): Observable<void> {
    return this.http.post<void>(`/v1/public/sessions/${sessionId}/reviews/${reviewId}/public-opened`, {});
  }
}

/** Dialling codes offered on the feedback form, the venue's own country first. */
export const DIAL_CODES = ['+971', '+966', '+974', '+965', '+973', '+968', '+962', '+20', '+44', '+1'];

const CODE_BY_CURRENCY: Record<string, string> = {
  AED: '+971', SAR: '+966', QAR: '+974', KWD: '+965', BHD: '+973', OMR: '+968', JOD: '+962', EGP: '+20', GBP: '+44', USD: '+1'
};

export function dialCodeFor(currency: string): string {
  return CODE_BY_CURRENCY[currency] ?? '+971';
}
