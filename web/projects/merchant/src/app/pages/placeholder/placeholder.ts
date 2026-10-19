import { Component, input } from '@angular/core';

/** Stand-in page for a merchant area that a later sprint will build. */
@Component({
  selector: 'df-placeholder',
  template: `
    <h1 class="text-2xl font-bold">{{ title() }}</h1>
    <div class="mt-6 rounded-xl border border-dashed border-line bg-white p-10 text-center text-ink-muted">
      Coming in Sprint {{ sprint() }}.
    </div>
  `
})
export class Placeholder {
  // Bound from route data through withComponentInputBinding().
  readonly title = input.required<string>();
  readonly sprint = input.required<number>();
}
