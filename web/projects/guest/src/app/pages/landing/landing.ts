import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { GuestContext, GuestSession, errorCode } from '../../core/guest-session';
import { I18n } from '../../core/i18n';
import { formatMoney } from '../../core/menu';
import { Bill, PaymentsApi } from '../../core/payments';

type State =
  | { kind: 'loading' }
  | { kind: 'ready'; context: GuestContext }
  | { kind: 'error'; code: string };

/**
 * QR landing (blueprint 4.2 step 1, mockup "QR landing"): venue branding, table context and
 * the actions this venue supports, with what the table still owes on Pay Bill. Invalid or paused
 * QR codes get a recovery message instead of a technical error.
 */
@Component({
  selector: 'hz-landing',
  imports: [RouterLink],
  templateUrl: './landing.html'
})
export class Landing implements OnInit {
  private readonly session = inject(GuestSession);
  private readonly payments = inject(PaymentsApi);
  protected readonly i18n = inject(I18n);

  /** From the route: /q/:token */
  readonly token = input.required<string>();

  protected readonly state = signal<State>({ kind: 'loading' });
  /** The table's bill while something is left to pay on it. */
  private readonly owing = signal<Bill | null>(null);
  /** What's left to pay, e.g. "JOD 12.500", in the guest's language; null when nothing is owed. */
  protected readonly due = computed(() => {
    const bill = this.owing();
    return bill ? formatMoney(bill.remainingMinor, bill.currency, this.i18n.lang()) : null;
  });

  protected readonly context = computed(() => {
    const state = this.state();
    return state.kind === 'ready' ? state.context : null;
  });

  protected readonly locationLabel = computed(() => {
    const ctx = this.context();
    if (!ctx) return '';
    const type = this.i18n.t().locationTypes[ctx.location.type] ?? '';
    const name = `${type} ${ctx.location.label}`.trim();
    return ctx.location.areaName ? `${name} · ${this.i18n.localize(ctx.location.areaName, ctx.location.areaNameAr)}` : name;
  });

  protected readonly initials = computed(() =>
    (this.context()?.venue.brandName ?? '')
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map(word => word[0]!.toUpperCase())
      .join('')
  );

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.state.set({ kind: 'loading' });
    // Only a language the guest picked overrides the venue's default (e.g. Arabic in Amman).
    this.session.open(this.token(), this.i18n.hasPreference() ? this.i18n.lang() : null).subscribe({
      next: context => {
        this.i18n.useDefault(context.locale);
        this.state.set({ kind: 'ready', context });
        if (context.features.payAtTable) this.loadBill(context);
      },
      error: error => this.state.set({ kind: 'error', code: errorCode(error) })
    });
  }

  private loadBill(context: GuestContext): void {
    this.payments.bill(context.sessionId).subscribe({
      next: bill => this.owing.set(bill.status === 'Open' && bill.remainingMinor > 0 ? bill : null),
      error: () => this.owing.set(null) // Pay Bill still works without the amount
    });
  }
}
