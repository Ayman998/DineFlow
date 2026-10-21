import { Component, computed, inject, input, output, signal } from '@angular/core';

import { GuestSession, errorCode } from '../../core/guest-session';
import { I18n } from '../../core/i18n';
import { safeUrl } from '../../core/payments';
import { DIAL_CODES, ReviewResult, ReviewsApi, dialCodeFor } from '../../core/reviews';

type Step = 'rate' | 'public' | 'feedback';

/**
 * Rating the visit after paying (blueprint 4.2 step 10, mockups "Rate your experience",
 * "Public review prompt" and "Private feedback form"): 4–5 stars are offered the venue's public
 * review page, 1–3 stars get a private form that goes only to the venue. Closing at any point
 * leaves the payment and receipt as they are.
 */
@Component({
  selector: 'df-review-flow',
  templateUrl: './review-flow.html'
})
export class ReviewFlow {
  private readonly api = inject(ReviewsApi);
  private readonly session = inject(GuestSession);
  protected readonly i18n = inject(I18n);

  readonly paymentId = input.required<string>();
  /** The rating has been saved (and any feedback sent); the confirmation shows it. */
  readonly done = output<ReviewResult | null>();

  protected readonly dialCodes = DIAL_CODES;
  protected readonly stars = [1, 2, 3, 4, 5];
  protected readonly step = signal<Step>('rate');
  protected readonly rating = signal(0);
  protected readonly comment = signal('');
  protected readonly result = signal<ReviewResult | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly name = signal('');
  protected readonly email = signal('');
  protected readonly dialCode = signal(dialCodeFor(this.session.current()?.context.venue.currency ?? 'AED'));
  protected readonly phone = signal('');
  protected readonly consent = signal(false);

  protected readonly venueName = computed(() => this.session.current()?.context.venue.brandName ?? '');
  protected readonly hasContact = computed(() => !!(this.name().trim() || this.email().trim() || this.phone().trim()));

  protected ratingWord(n: number): string {
    return this.i18n.t().ratingWords[n] ?? '';
  }

  protected submit(): void {
    const sessionId = this.sessionId();
    if (!sessionId || !this.rating() || this.busy()) return;

    this.busy.set(true);
    this.error.set(null);
    this.api.submit(sessionId, this.paymentId(), this.rating(), this.comment().trim() || null).subscribe({
      next: result => {
        this.busy.set(false);
        this.result.set(result);
        if (result.positive && result.publicReview && safeUrl(result.publicReview.url)) this.step.set('public');
        else if (!result.positive && !result.feedbackSent) this.step.set('feedback');
        else this.done.emit(result);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.error.set(errorCode(error) === 'reviews_disabled' ? null : this.i18n.t().reviewFailed);
        if (errorCode(error) === 'reviews_disabled') this.done.emit(null);
      }
    });
  }

  /** Opens the venue's review page in a new tab and records that the guest went there. */
  protected openPublic(): void {
    const result = this.result();
    const url = safeUrl(result?.publicReview?.url);
    const sessionId = this.sessionId();
    if (!result || !url || !sessionId) return;
    window.open(url, '_blank', 'noopener');
    this.api.publicOpened(sessionId, result.id).subscribe({ error: () => undefined });
    this.done.emit(result);
  }

  protected sendFeedback(): void {
    const result = this.result();
    const sessionId = this.sessionId();
    if (!result || !sessionId || this.busy()) return;

    const t = this.i18n.t();
    if (this.hasContact() && !this.consent()) {
      this.error.set(t.consentNeeded);
      return;
    }
    if (this.consent() && !this.email().trim() && !this.phone().trim()) {
      this.error.set(t.contactNeeded);
      return;
    }

    const phone = this.phone().trim();
    this.busy.set(true);
    this.error.set(null);
    this.api.feedback(sessionId, result.id, {
      comment: this.comment().trim() || null,
      name: this.consent() ? this.name().trim() || null : null,
      email: this.consent() ? this.email().trim() || null : null,
      phone: this.consent() && phone ? `${this.dialCode()} ${phone}` : null,
      consent: this.consent()
    }).subscribe({
      next: sent => {
        this.busy.set(false);
        this.done.emit(sent);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.error.set(errorCode(error) === 'invalid_feedback' ? t.contactNeeded : t.feedbackFailed);
      }
    });
  }

  protected close(): void {
    this.done.emit(this.result());
  }

  private sessionId(): string | undefined {
    return this.session.current()?.context.sessionId;
  }
}
