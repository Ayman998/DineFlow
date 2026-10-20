import { Location } from '@angular/common';
import { Component, inject, input } from '@angular/core';

import { I18n } from '../../core/i18n';

/** Stand-in for the menu and bill screens until their sprints land. */
@Component({
  selector: 'df-coming-soon',
  template: `
    @let t = i18n.t();
    <main class="flex min-h-dvh flex-col items-center justify-center gap-3 px-8 text-center">
      <h1 class="text-2xl font-extrabold">{{ feature() === 'menu' ? t.menuSoonTitle : t.billSoonTitle }}</h1>
      <p class="text-ink-muted">{{ feature() === 'menu' ? t.menuSoonBody : t.billSoonBody }}</p>
      <button type="button" class="mt-4 min-h-12 rounded-[14px] bg-brand px-6 font-bold text-white" (click)="back()">
        {{ t.back }}
      </button>
    </main>
  `
})
export class ComingSoon {
  private readonly location = inject(Location);
  protected readonly i18n = inject(I18n);

  /** From route data. */
  readonly feature = input.required<'menu' | 'bill'>();

  protected back(): void {
    this.location.back();
  }
}
