import { Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';

import { I18n } from '../core/i18n';
import { LEGAL, LegalDoc } from '../pages/legal/legal.i18n';

/** Terms · Privacy · Refunds · Contact, at the foot of guest pages (acquirers look for these). */
@Component({
  selector: 'hz-legal-links',
  imports: [RouterLink],
  template: `
    <nav class="flex flex-wrap items-center justify-center gap-x-1 text-xs" [class]="dark() ? 'text-white/60' : 'text-ink-muted'" [attr.aria-label]="label()">
      @for (doc of docs; track doc; let last = $last) {
        <a [routerLink]="'/' + doc" class="inline-flex min-h-8 items-center px-1 underline-offset-2 hover:underline">{{ names()[doc] }}</a>
        @if (!last) { <span aria-hidden="true">·</span> }
      }
    </nav>
  `
})
export class LegalLinks {
  private readonly i18n = inject(I18n);

  /** White text, for the dark home page. */
  readonly dark = input(false);

  protected readonly docs: LegalDoc[] = ['terms', 'privacy', 'refunds', 'contact'];
  protected readonly names = computed(() => LEGAL[this.i18n.lang()].nav);
  protected readonly label = computed(() => LEGAL[this.i18n.lang()].titles.terms);
}
