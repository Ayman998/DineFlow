import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';

import { PaymentLinkDetail, PaymentLinkFilter, PaymentLinkPage, PaymentLinkRow, PaymentLinkStatus, PaymentLinksApi, TimelineEvent, ValidityUnit } from '../../core/api';
import { errorMessage } from '../../core/auth';
import { CurrentVenue } from '../../core/current-venue';
import { I18n } from '../../core/i18n';
import { LiveOrders } from '../../core/live-orders';
import { formatMoney, parseMajor } from '../../core/money';
import { QrCode, qrDataUrl } from '../../shared/qr-code';
import { TIMELINE, timelineText } from '../../shared/timeline.i18n';
import { PAYMENT_LINKS } from './payment-links.i18n';

const HOUR = 60 * 60 * 1000;
/** The longest a link can stay active (90 days), as the API enforces. */
const MAX_HOURS = 90 * 24;

/**
 * Payment Links (blueprint 4.4, E16, mockups "Create payment link" and "Share payment link"):
 * create a link for an amount, share it by QR, WhatsApp, e-mail or the device's share sheet,
 * follow its status and payments, and deactivate it. A link closes by itself once it's paid in full.
 */
@Component({
  selector: 'df-payment-links',
  imports: [QrCode, RouterLink],
  templateUrl: './payment-links.html'
})
export class PaymentLinks implements OnInit {
  private readonly api = inject(PaymentLinksApi);
  private readonly venue = inject(CurrentVenue);
  private readonly live = inject(LiveOrders);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly i18n = inject(I18n);
  protected readonly t = this.i18n.use(PAYMENT_LINKS);
  private readonly steps = this.i18n.use(TIMELINE);

  protected readonly filters: PaymentLinkFilter[] = ['All', 'Active', 'Paid', 'Expired', 'Cancelled'];
  protected readonly status = signal<PaymentLinkFilter>('All');
  protected readonly search = signal('');
  protected readonly page = signal(1);
  protected readonly data = signal<PaymentLinkPage | null>(null);
  protected readonly loading = signal(false);
  protected readonly exporting = signal(false);
  protected readonly error = signal<string | null>(null);

  // The create form (mockup "Create payment link").
  protected readonly createOpen = signal(false);
  protected readonly allowSplit = signal(false);
  protected readonly requestTip = signal(false);
  protected readonly amount = signal('');
  protected readonly reference = signal('');
  protected readonly validFor = signal('24');
  protected readonly unit = signal<ValidityUnit>('Hours');
  protected readonly notifyCustomer = signal(false);
  protected readonly email = signal('');
  protected readonly creating = signal(false);
  protected readonly createError = signal<string | null>(null);
  private createRequestId: string | null = null;

  // An open link: share it, follow it, deactivate it.
  protected readonly detail = signal<PaymentLinkDetail | null>(null);
  protected readonly justCreated = signal(false);
  protected readonly copied = signal(false);
  protected readonly sendTo = signal('');
  protected readonly sending = signal(false);
  protected readonly working = signal(false);
  protected readonly detailError = signal<string | null>(null);
  protected readonly detailNotice = signal<string | null>(null);
  protected readonly canShare = typeof navigator !== 'undefined' && 'share' in navigator;

  private searchTimer: ReturnType<typeof setTimeout> | undefined;

  protected readonly currency = computed(() => this.venue.venue()?.currency ?? 'AED');

  /** Hours the link will be active for, or null when the entry isn't valid. */
  protected readonly validHours = computed(() => {
    const value = Number(this.validFor());
    if (!Number.isInteger(value) || value < 1) return null;
    const hours = this.unit() === 'Days' ? value * 24 : value;
    return hours <= MAX_HOURS ? hours : null;
  });

  /** "Link expires on 04 Oct 2026, 12:53", from now. */
  protected readonly expiresPreview = computed(() => {
    const hours = this.validHours();
    return hours === null ? null : this.dateTime(new Date(Date.now() + hours * HOUR).toISOString());
  });

  ngOnInit(): void {
    this.venue.load().subscribe({ next: () => this.load(), error: error => this.error.set(errorMessage(error)) });
    this.live.linkPaid.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(notice => {
      this.load();
      if (this.detail()?.summary.id === notice.paymentLinkId) this.open(notice.paymentLinkId);
    });
    this.live.reconnected.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.load());
    this.destroyRef.onDestroy(() => clearTimeout(this.searchTimer));
  }

  protected setStatus(status: PaymentLinkFilter): void {
    this.status.set(status);
    this.page.set(1);
    this.load();
  }

  protected setSearch(text: string): void {
    this.search.set(text);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => {
      this.page.set(1);
      this.load();
    }, 300);
  }

  protected goTo(page: number): void {
    this.page.set(page);
    this.load();
  }

  protected count(filter: PaymentLinkFilter): number {
    const counts = this.data()?.counts;
    if (!counts) return 0;
    return { All: counts.all, Active: counts.active, Paid: counts.paid, Expired: counts.expired, Cancelled: counts.cancelled }[filter];
  }

  protected exportCsv(): void {
    const venueId = this.venue.venue()?.id;
    if (!venueId || this.exporting()) return;
    this.exporting.set(true);
    this.api.export({ venueId, status: this.status(), search: this.search().trim() || undefined }).subscribe({
      next: blob => {
        this.exporting.set(false);
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `payment-links-${new Date().toISOString().slice(0, 10)}.csv`;
        a.click();
        URL.revokeObjectURL(url);
      },
      error: error => {
        this.exporting.set(false);
        this.error.set(errorMessage(error));
      }
    });
  }

  // ---- Create ----

  protected startCreate(): void {
    this.detail.set(null);
    this.allowSplit.set(false);
    this.requestTip.set(false);
    this.amount.set('');
    this.reference.set('');
    this.validFor.set('24');
    this.unit.set('Hours');
    this.notifyCustomer.set(false);
    this.email.set('');
    this.createError.set(null);
    this.createRequestId = crypto.randomUUID();
    this.createOpen.set(true);
  }

  protected create(): void {
    const venueId = this.venue.venue()?.id;
    if (!venueId || this.creating()) return;

    const amountMinor = parseMajor(this.amount(), this.currency());
    if (!amountMinor) return this.createError.set(this.t().amountInvalid(this.currency()));
    if (this.validHours() === null) return this.createError.set(this.t().validityInvalid);
    const email = this.email().trim();
    if (this.notifyCustomer() && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) return this.createError.set(this.t().emailInvalid);

    this.creating.set(true);
    this.createError.set(null);
    this.api.create({
      venueId,
      clientRequestId: this.createRequestId ?? crypto.randomUUID(),
      amountMinor,
      reference: this.reference().trim() || null,
      allowSplit: this.allowSplit(),
      requestTip: this.requestTip(),
      validFor: Number(this.validFor()),
      validityUnit: this.unit(),
      customerEmail: this.notifyCustomer() ? email : null
    }).subscribe({
      next: link => {
        this.creating.set(false);
        this.createOpen.set(false);
        this.showDetail(link, true);
        if (this.notifyCustomer() && !link.customerNotifiedAt)
          this.detailError.set(this.t().emailNotSent);
        this.load();
      },
      error: error => {
        this.creating.set(false);
        this.createError.set(errorMessage(error));
      }
    });
  }

  // ---- An open link ----

  protected open(id: string): void {
    this.api.get(id).subscribe({
      next: link => this.showDetail(link, this.justCreated() && this.detail()?.summary.id === id),
      error: error => this.error.set(errorMessage(error))
    });
  }

  protected close(): void {
    this.detail.set(null);
    this.createOpen.set(false);
  }

  protected copy(link: PaymentLinkRow): void {
    navigator.clipboard?.writeText(link.url).then(() => {
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 2_000);
    }).catch(() => this.detailError.set(this.t().copyFailed));
  }

  protected async downloadQr(link: PaymentLinkRow): Promise<void> {
    const a = document.createElement('a');
    a.href = await qrDataUrl(link.url, 1024);
    a.download = `${this.venue.venue()?.name ?? 'venue'} - ${link.code}.png`;
    a.click();
  }

  protected share(link: PaymentLinkRow): void {
    navigator.share?.({ title: this.t().shareTitle(link.code), text: this.message(link, false), url: link.url }).catch(() => undefined);
  }

  protected whatsAppUrl(link: PaymentLinkRow): string {
    return `https://wa.me/?text=${encodeURIComponent(this.message(link, true))}`;
  }

  protected mailtoUrl(link: PaymentLinkRow): string {
    const subject = this.t().mailSubject(this.venue.venue()?.name ?? null, link.code);
    return `mailto:${encodeURIComponent(link.customerEmail ?? '')}?subject=${encodeURIComponent(subject)}&body=${encodeURIComponent(this.message(link, true))}`;
  }

  protected sendEmail(): void {
    const link = this.detail();
    const to = this.sendTo().trim();
    if (!link || this.sending()) return;
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(to)) return this.detailError.set(this.t().emailInvalid);

    this.sending.set(true);
    this.detailError.set(null);
    this.api.notify(link.summary.id, to).subscribe({
      next: updated => {
        this.sending.set(false);
        this.showDetail(updated, this.justCreated());
        this.detailNotice.set(this.t().sentTo(to));
        this.load();
      },
      error: error => {
        this.sending.set(false);
        this.detailError.set(errorMessage(error));
      }
    });
  }

  protected deactivate(): void {
    const link = this.detail();
    if (!link || this.working()) return;
    if (!confirm(this.t().confirmDeactivate(link.summary.code))) return;

    this.working.set(true);
    this.detailError.set(null);
    this.api.cancel(link.summary.id).subscribe({
      next: updated => {
        this.working.set(false);
        this.showDetail(updated, false);
        this.load();
      },
      error: error => {
        this.working.set(false);
        this.detailError.set(errorMessage(error));
      }
    });
  }

  // ---- Display ----

  protected statusLabel(status: PaymentLinkFilter): string {
    return this.t().statuses[status] ?? status;
  }

  protected statusClass(status: PaymentLinkStatus): string {
    switch (status) {
      case 'Active': return 'bg-[#EEF4FF] text-[#3538CD]';
      case 'Paid': return 'bg-emerald-50 text-emerald-700';
      case 'Expired': return 'bg-amber-50 text-amber-800';
      default: return 'bg-surface text-ink-muted';
    }
  }

  protected paymentState(state: string): string {
    return this.t().paymentStates[state] ?? state;
  }

  protected method(method: string): string {
    return this.t().methods[method] ?? method;
  }

  /** A timeline step in the portal's language. */
  protected step(event: TimelineEvent, currency: string): string {
    return timelineText(event, this.steps(), currency);
  }

  protected money(minor: number, currency = this.currency()): string {
    return formatMoney(minor, currency);
  }

  protected dateTime(iso: string | null): string {
    return iso ? new Date(iso).toLocaleString(this.i18n.locale(), { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' }) : '–';
  }

  protected lastRow(d: PaymentLinkPage): number {
    return Math.min(d.total, d.page * d.pageSize);
  }

  private showDetail(link: PaymentLinkDetail, justCreated: boolean): void {
    if (this.detail()?.summary.id !== link.summary.id) {
      this.sendTo.set(link.summary.customerEmail ?? '');
      this.detailError.set(null);
      this.detailNotice.set(null);
    }
    this.justCreated.set(justCreated);
    this.detail.set(link);
  }

  /** What goes with the link when it's shared, e.g. on WhatsApp. */
  private message(link: PaymentLinkRow, withUrl: boolean): string {
    return this.t().message(this.venue.venue()?.name ?? null, this.money(link.amountMinor, link.currency), link.reference, withUrl ? link.url : null);
  }

  private load(): void {
    const venueId = this.venue.venue()?.id;
    if (!venueId) return;
    this.loading.set(true);
    this.api.list({ venueId, status: this.status(), search: this.search().trim() || undefined, page: this.page(), pageSize: 25 }).subscribe({
      next: page => {
        this.loading.set(false);
        this.error.set(null);
        this.data.set(page);
      },
      error: error => {
        this.loading.set(false);
        this.error.set(errorMessage(error));
      }
    });
  }
}
