import { Component, computed, inject, input } from '@angular/core';

import { I18n } from '../core/i18n';

/**
 * The order journey: Selection, Ordered, Ready, Served and Check Out (P1-ORD-05). Steps before
 * the current one show a tick; the current one is highlighted. The steps share the width, so all
 * five fit on a phone, with the line between them running from circle to circle.
 */
@Component({
  selector: 'hz-order-tracker',
  template: `
    @let t = i18n.t();
    <ol class="flex items-start" [attr.aria-label]="t.orderProgress">
      @for (label of labels(); track $index; let i = $index; let last = $last) {
        <li class="relative min-w-0 flex-1 text-center" [attr.aria-current]="i === stage() ? 'step' : null">
          @if (!last) {
            <span aria-hidden="true" class="absolute start-[calc(50%+15px)] end-[calc(15px-50%)] top-[10px] h-0.5"
                  [class]="i < stage() ? 'bg-brand' : 'bg-[#D4D4D4]'"></span>
          }
          <span class="relative mx-auto flex size-[22px] items-center justify-center rounded-full text-[11px] font-extrabold"
                [class]="i < stage() ? 'bg-brand/15 text-brand' : i === stage() ? 'bg-accent text-ink' : 'border-2 border-[#D4D4D4] text-ink-muted'">
            {{ i < stage() ? '✓' : i + 1 }}
          </span>
          <span class="mt-1.5 block text-[11px] leading-tight font-semibold whitespace-nowrap" [class]="i <= stage() ? 'text-ink' : 'text-ink-muted'">{{ label }}</span>
        </li>
      }
    </ol>
  `
})
export class OrderTracker {
  protected readonly i18n = inject(I18n);

  /** 0 = Selection … 4 = Check Out. */
  readonly stage = input.required<number>();

  protected readonly labels = computed(() => {
    const t = this.i18n.t();
    return [t.stageSelection, t.stageOrdered, t.stageReady, t.stageServed, t.stageCheckOut];
  });
}
