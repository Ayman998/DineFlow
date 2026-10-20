import { Component, computed, inject, input } from '@angular/core';

import { I18n } from '../core/i18n';

/**
 * The four-step progress tracker from the mockup: Selection, Checkout, Preparing, Delivered
 * (P1-ORD-05). Steps before the current one show a tick; the current one is highlighted.
 */
@Component({
  selector: 'df-order-tracker',
  template: `
    @let t = i18n.t();
    <ol class="flex items-start" [attr.aria-label]="t.orderProgress">
      @for (label of labels(); track $index; let i = $index; let last = $last) {
        <li class="w-[62px] shrink-0 text-center" [attr.aria-current]="i === stage() ? 'step' : null">
          <span class="mx-auto flex size-[22px] items-center justify-center rounded-full text-[11px] font-extrabold"
                [class]="i < stage() ? 'bg-brand/15 text-brand' : i === stage() ? 'bg-brand text-white' : 'border-2 border-[#D0D5DD] text-ink-muted'">
            {{ i < stage() ? '✓' : i + 1 }}
          </span>
          <span class="mt-1.5 block text-[11px] font-semibold" [class]="i <= stage() ? 'text-ink' : 'text-ink-muted'">{{ label }}</span>
        </li>
        @if (!last) {
          <li aria-hidden="true" class="mx-1 mt-[10px] h-0.5 flex-1" [class]="i < stage() ? 'bg-brand' : 'bg-[#D0D5DD]'"></li>
        }
      }
    </ol>
  `
})
export class OrderTracker {
  protected readonly i18n = inject(I18n);

  /** 0 = Selection … 3 = Delivered. */
  readonly stage = input.required<number>();

  protected readonly labels = computed(() => {
    const t = this.i18n.t();
    return [t.stageSelection, t.stageCheckout, t.stagePreparing, t.stageDelivered];
  });
}
