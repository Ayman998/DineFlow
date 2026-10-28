import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import { ReviewContact, ReviewDetail, ReviewKind, ReviewPage, ReviewQuery, ReviewSort, ReviewStats, ReviewsAdminApi, StaffRating } from '../../core/api';
import { Auth, errorMessage } from '../../core/auth';
import { CurrentVenue } from '../../core/current-venue';
import { I18n } from '../../core/i18n';
import { LiveOrders } from '../../core/live-orders';
import { formatMoney } from '../../core/money';
import { REVIEWS } from './reviews.i18n';

const DAY = 24 * 60 * 60 * 1000;

/**
 * Reviews (blueprint 4.4, mockups "Reviews dashboard" and "Review detail"; P1-REV-07…09,
 * P1-STAFF-03, P1-MER-11): headline numbers against the previous period, ratings per star, the
 * filtered list, each review with its visit, and ratings per staff member.
 */
@Component({
  selector: 'df-reviews',
  templateUrl: './reviews.html'
})
export class Reviews implements OnInit {
  private readonly api = inject(ReviewsAdminApi);
  private readonly venue = inject(CurrentVenue);
  private readonly live = inject(LiveOrders);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly auth = inject(Auth);
  protected readonly i18n = inject(I18n);
  protected readonly t = this.i18n.use(REVIEWS);

  protected readonly ranges = [7, 30, 90];
  protected readonly pageSizes = [10, 25, 50];
  protected readonly days = signal(30);
  protected readonly kind = signal<ReviewKind>('All');
  protected readonly rating = signal<number | null>(null);
  protected readonly sort = signal<ReviewSort>('Recent');
  protected readonly withComment = signal(false);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(10);

  protected readonly data = signal<ReviewPage | null>(null);
  protected readonly staff = signal<StaffRating[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly exporting = signal(false);

  protected readonly detail = signal<ReviewDetail | null>(null);
  protected readonly contact = signal<ReviewContact | null>(null);
  protected readonly detailError = signal<string | null>(null);

  protected readonly venueName = computed(() => this.venue.venue()?.name ?? '');
  protected readonly maxStar = computed(() => Math.max(1, ...(this.data()?.summary.perStar ?? [1])));
  protected readonly lastRow = computed(() => {
    const d = this.data();
    return d ? Math.min(d.total, d.page * d.pageSize) : 0;
  });

  ngOnInit(): void {
    this.venue.load().subscribe({ next: () => this.load(), error: error => this.error.set(errorMessage(error)) });
    // A guest has just rated their visit: show it.
    this.live.reviewed.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.load());
  }

  protected setRange(days: number): void {
    this.days.set(days);
    this.reload();
  }

  protected setKind(kind: ReviewKind): void {
    this.kind.set(kind);
    this.rating.set(null);
    this.reload();
  }

  protected setRating(value: string): void {
    this.rating.set(value ? Number(value) : null);
    this.reload();
  }

  protected setSort(value: string): void {
    this.sort.set(value as ReviewSort);
    this.reload();
  }

  protected toggleComments(): void {
    this.withComment.set(!this.withComment());
    this.reload();
  }

  protected setPageSize(value: string): void {
    this.pageSize.set(Number(value));
    this.reload();
  }

  protected goTo(page: number): void {
    this.page.set(page);
    this.load();
  }

  protected export(): void {
    const query = this.query();
    if (!query) return;
    this.exporting.set(true);
    this.api.export(query).subscribe({
      next: blob => {
        this.exporting.set(false);
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `reviews-${new Date().toISOString().slice(0, 10)}.csv`;
        a.click();
        URL.revokeObjectURL(url);
      },
      error: error => {
        this.exporting.set(false);
        this.error.set(errorMessage(error));
      }
    });
  }

  protected open(id: string): void {
    this.contact.set(null);
    this.detailError.set(null);
    this.api.get(id).subscribe({ next: d => this.detail.set(d), error: error => this.error.set(errorMessage(error)) });
  }

  protected close(): void {
    this.detail.set(null);
    this.contact.set(null);
  }

  /** Managers only; the server records each view. */
  protected showContact(): void {
    const d = this.detail();
    if (!d) return;
    this.api.contact(d.id).subscribe({ next: c => this.contact.set(c), error: error => this.detailError.set(errorMessage(error)) });
  }

  protected followUp(): void {
    const d = this.detail();
    if (!d) return;
    this.api.followUp(d.id).subscribe({
      next: updated => {
        this.detail.set(updated);
        this.data.update(page => page && { ...page, items: page.items.map(r => (r.id === updated.id ? { ...r, followedUp: true } : r)) });
      },
      error: error => this.detailError.set(errorMessage(error))
    });
  }

  protected stars(n: number): string {
    return '★'.repeat(n);
  }

  protected percent(share: number | null): string {
    return share === null ? '–' : `${Math.round(share * 100)}%`;
  }

  /** "+12 vs previous 30 days", or nothing to compare with. */
  protected change(current: number | null, previous: number | null, kind: 'count' | 'average' | 'share'): string | null {
    if (current === null || previous === null) return null;
    const diff = current - previous;
    const t = this.t();
    const text = kind === 'share' ? t.points(Math.round(Math.abs(diff) * 100)) : kind === 'average' ? Math.abs(diff).toFixed(2) : String(Math.abs(diff));
    return `${diff > 0 ? '▲' : diff < 0 ? '▼' : '='} ${diff === 0 ? t.same : text} ${t.vsPrevious(this.days())}`;
  }

  protected average(stats: ReviewStats): string {
    return stats.average === null ? '–' : stats.average.toFixed(2);
  }

  protected when(iso: string): string {
    const d = new Date(iso);
    const locale = this.i18n.locale();
    return `${d.toLocaleDateString(locale, { day: '2-digit', month: 'short' })} · ${d.toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit' })}`;
  }

  protected whenFull(iso: string | null): string {
    return iso ? new Date(iso).toLocaleString(this.i18n.locale(), { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' }) : '–';
  }

  /** "Table 12", with the location type in the portal's language. */
  protected location(type: string, label: string): string {
    return `${this.i18n.t().locationTypes[type] ?? type} ${label}`;
  }

  protected method(method: string): string {
    return this.t().methods[method] ?? method;
  }

  protected paymentStatus(status: string): string {
    return this.t().paymentStatuses[status] ?? status;
  }

  protected money(minor: number, currency: string): string {
    return formatMoney(minor, currency);
  }

  private reload(): void {
    this.page.set(1);
    this.load();
  }

  private query(): ReviewQuery | null {
    const venueId = this.venue.venue()?.id;
    if (!venueId) return null;
    const to = new Date();
    return {
      venueId,
      from: new Date(to.getTime() - this.days() * DAY).toISOString(),
      to: to.toISOString(),
      kind: this.kind(),
      rating: this.rating(),
      withComment: this.withComment(),
      sort: this.sort(),
      page: this.page(),
      pageSize: this.pageSize()
    };
  }

  private load(): void {
    const query = this.query();
    if (!query) return;
    this.error.set(null);
    this.api.list(query).subscribe({ next: page => this.data.set(page), error: error => this.error.set(errorMessage(error)) });
    this.api.staffSummary(query.venueId, query.from, query.to).subscribe({ next: s => this.staff.set(s), error: () => this.staff.set([]) });
  }
}
