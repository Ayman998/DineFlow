import { Component, computed, inject, input } from '@angular/core';

import { I18n } from '../../core/i18n';

/** Stand-in page for a merchant area that a later sprint will build. */
@Component({
  selector: 'df-placeholder',
  template: `
    <h1 class="text-2xl font-bold">{{ title() }}</h1>
    <div class="mt-6 rounded-xl border border-dashed border-line bg-white p-10 text-center text-ink-muted">
      {{ t().comingSoon(sprint()) }}
    </div>
  `
})
export class Placeholder {
  protected readonly t = inject(I18n).t;
  // Bound from route data through withComponentInputBinding().
  readonly titleKey = input.required<string>();
  readonly sprint = input.required<number>();
  protected readonly title = computed(() => this.t().nav[this.titleKey()] ?? this.titleKey());
}
