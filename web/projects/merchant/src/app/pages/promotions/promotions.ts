import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';

import { DiscountType, PromoCode, PromoCodeRequest, PromotionsApi } from '../../core/api';
import { Auth, errorMessage } from '../../core/auth';
import { CurrentVenue } from '../../core/current-venue';
import { I18n } from '../../core/i18n';
import { formatMoney, parseMajor, toMajorString } from '../../core/money';
import { PROMOTIONS } from './promotions.i18n';

/**
 * Promotion and voucher codes (blueprint E30, P1-PAY-08): codes guests enter at checkout, and the
 * offer shown on the payment confirmation for their next visit (P1-REV-01). Codes are checked on
 * the server; each successful payment that used one counts as a redemption.
 */
@Component({
  selector: 'df-promotions',
  imports: [ReactiveFormsModule],
  templateUrl: './promotions.html'
})
export class Promotions implements OnInit {
  private readonly api = inject(PromotionsApi);
  private readonly venue = inject(CurrentVenue);
  protected readonly auth = inject(Auth);
  protected readonly i18n = inject(I18n);
  protected readonly t = this.i18n.use(PROMOTIONS);

  protected readonly codes = signal<PromoCode[] | null>(null);
  protected readonly loadError = signal<string | null>(null);
  /** The code being edited; 'new' while creating one. */
  protected readonly editing = signal<PromoCode | 'new' | null>(null);
  protected readonly saving = signal(false);
  protected readonly saveError = signal<string | null>(null);

  protected readonly currency = computed(() => this.venue.venue()?.currency ?? 'AED');

  protected readonly form = inject(FormBuilder).nonNullable.group({
    code: ['', [Validators.required, Validators.pattern(/^[A-Za-z0-9-]{3,20}$/)]],
    headline: ['', [Validators.required, Validators.maxLength(120)]],
    type: ['Percent' as DiscountType, Validators.required],
    value: ['10', Validators.required],
    maxDiscount: [''],
    minBill: [''],
    startsAt: [''],
    endsAt: [''],
    maxRedemptions: [''],
    isActive: [true],
    showAfterPayment: [false]
  });

  ngOnInit(): void {
    this.venue.load().subscribe({ error: () => undefined });
    this.load();
  }

  protected discountText(p: PromoCode): string {
    const t = this.t();
    if (p.type === 'Percent') {
      const cap = p.maxDiscountMinor ? t.upTo(formatMoney(p.maxDiscountMinor, this.currency())) : '';
      return `${t.percentOff(p.value / 100)}${cap}`;
    }
    return t.amountOff(formatMoney(p.value, this.currency()));
  }

  protected limitsText(p: PromoCode): string {
    const t = this.t();
    const parts: string[] = [];
    if (p.minBillMinor) parts.push(t.minBillLimit(formatMoney(p.minBillMinor, this.currency())));
    if (p.startsAt) parts.push(t.from(this.date(p.startsAt)));
    if (p.endsAt) parts.push(t.until(this.date(p.endsAt)));
    return parts.join(' · ') || t.noLimits;
  }

  protected status(p: PromoCode): { label: string; tone: 'on' | 'off' | 'warn' } {
    const t = this.t();
    const now = Date.now();
    if (!p.isActive) return { label: t.paused, tone: 'off' };
    if (p.endsAt && new Date(p.endsAt).getTime() <= now) return { label: t.expired, tone: 'off' };
    if (p.maxRedemptions && p.redemptionCount >= p.maxRedemptions) return { label: t.usedUp, tone: 'off' };
    if (p.startsAt && new Date(p.startsAt).getTime() > now) return { label: t.scheduled, tone: 'warn' };
    return { label: t.active, tone: 'on' };
  }

  protected open(code: PromoCode | 'new'): void {
    this.saveError.set(null);
    this.editing.set(code);
    if (code === 'new') {
      this.form.reset();
      this.form.controls.code.enable();
      return;
    }

    const currency = this.currency();
    this.form.reset({
      code: code.code,
      headline: code.headline,
      type: code.type,
      value: code.type === 'Percent' ? String(code.value / 100) : toMajorString(code.value, currency),
      maxDiscount: code.maxDiscountMinor ? toMajorString(code.maxDiscountMinor, currency) : '',
      minBill: code.minBillMinor ? toMajorString(code.minBillMinor, currency) : '',
      startsAt: toLocalInput(code.startsAt),
      endsAt: toLocalInput(code.endsAt),
      maxRedemptions: code.maxRedemptions ? String(code.maxRedemptions) : '',
      isActive: code.isActive,
      showAfterPayment: code.showAfterPayment
    });
    // Guests may already have the code, so it can't be renamed.
    this.form.controls.code.disable();
  }

  protected close(): void {
    this.editing.set(null);
  }

  protected save(): void {
    const editing = this.editing();
    if (!editing) return;
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const request = this.toRequest();
    if (typeof request === 'string') {
      this.saveError.set(request);
      return;
    }

    this.saving.set(true);
    this.saveError.set(null);
    const call = editing === 'new' ? this.api.create(request) : this.api.update(editing.id, request);
    call.subscribe({
      next: saved => {
        this.codes.update(list => {
          const rest = (list ?? []).filter(c => c.id !== saved.id);
          return [...rest, saved].sort((a, b) => a.code.localeCompare(b.code));
        });
        this.saving.set(false);
        this.editing.set(null);
      },
      error: error => {
        this.saving.set(false);
        this.saveError.set(errorMessage(error));
      }
    });
  }

  protected invalid(control: keyof typeof this.form.controls): boolean {
    const c = this.form.controls[control];
    return c.touched && c.invalid;
  }

  private load(): void {
    this.api.list().subscribe({
      next: codes => this.codes.set(codes),
      error: error => this.loadError.set(errorMessage(error))
    });
  }

  /** The request to send, or a message saying what to fix. */
  private toRequest(): PromoCodeRequest | string {
    const v = this.form.getRawValue();
    const currency = this.currency();
    const t = this.t();

    let value: number;
    if (v.type === 'Percent') {
      const percent = Number(v.value);
      if (!/^\d+(\.\d{1,2})?$/.test(v.value.trim()) || percent <= 0 || percent > 100) return t.percentRange;
      value = Math.round(percent * 100);
    } else {
      const amount = parseMajor(v.value, currency);
      if (!amount) return t.amountInvalid(currency);
      value = amount;
    }

    const optionalMoney = (text: string, invalid: string): number | null | string => {
      if (!text.trim()) return null;
      return parseMajor(text, currency) ?? invalid;
    };
    const maxDiscount = v.type === 'Percent' ? optionalMoney(v.maxDiscount, t.maxDiscountInvalid(currency)) : null;
    if (typeof maxDiscount === 'string') return maxDiscount;
    if (maxDiscount === 0) return t.maxDiscountZero;
    const minBill = optionalMoney(v.minBill, t.minBillInvalid(currency));
    if (typeof minBill === 'string') return minBill;

    let maxRedemptions: number | null = null;
    if (v.maxRedemptions.trim()) {
      maxRedemptions = Number(v.maxRedemptions);
      if (!Number.isInteger(maxRedemptions) || maxRedemptions < 1) return t.redemptionLimitInvalid;
    }

    const startsAt = v.startsAt ? new Date(v.startsAt).toISOString() : null;
    const endsAt = v.endsAt ? new Date(v.endsAt).toISOString() : null;
    if (startsAt && endsAt && endsAt <= startsAt) return t.endBeforeStart;

    return {
      code: v.code.trim().toUpperCase(),
      headline: v.headline.trim(),
      type: v.type,
      value,
      maxDiscountMinor: maxDiscount,
      minBillMinor: minBill,
      startsAt,
      endsAt,
      maxRedemptions,
      isActive: v.isActive,
      showAfterPayment: v.showAfterPayment
    };
  }

  private date(iso: string): string {
    return new Date(iso).toLocaleDateString(this.i18n.locale(), { day: '2-digit', month: 'short', year: 'numeric' });
  }
}

/** An ISO time as the value of a datetime-local input, in the browser's time zone. */
function toLocalInput(iso: string | null): string {
  if (!iso) return '';
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}
