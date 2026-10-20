import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { GuestContext, GuestSession, errorCode } from '../../core/guest-session';
import { I18n } from '../../core/i18n';

type State =
  | { kind: 'loading' }
  | { kind: 'ready'; context: GuestContext }
  | { kind: 'error'; code: string };

/**
 * QR landing (blueprint 4.2 step 1, mockup "QR landing"): venue branding, table context and
 * the actions this venue supports. Invalid or paused QR codes get a recovery message instead
 * of a technical error.
 */
@Component({
  selector: 'df-landing',
  imports: [RouterLink],
  templateUrl: './landing.html'
})
export class Landing implements OnInit {
  private readonly session = inject(GuestSession);
  protected readonly i18n = inject(I18n);

  /** From the route: /q/:token */
  readonly token = input.required<string>();

  protected readonly state = signal<State>({ kind: 'loading' });

  protected readonly context = computed(() => {
    const state = this.state();
    return state.kind === 'ready' ? state.context : null;
  });

  protected readonly locationLabel = computed(() => {
    const ctx = this.context();
    if (!ctx) return '';
    const type = this.i18n.t().locationTypes[ctx.location.type] ?? '';
    const name = `${type} ${ctx.location.label}`.trim();
    return ctx.location.areaName ? `${name} · ${ctx.location.areaName}` : name;
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
    this.session.open(this.token(), this.i18n.lang()).subscribe({
      next: context => {
        this.i18n.useDefault(context.locale);
        this.state.set({ kind: 'ready', context });
      },
      error: error => this.state.set({ kind: 'error', code: errorCode(error) })
    });
  }
}
