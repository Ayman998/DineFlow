import { Component, computed, input } from '@angular/core';

/**
 * The helloZen logo from the brand board: the smiling "hz" mark with the wordmark and,
 * optionally, the "Pay · Split · Enjoy" tagline. It scales with the font size, so size it with a
 * text-* class. tone="dark" is for dark surfaces (yellow mark, white "hello"); tone="light" puts
 * the mark in a yellow badge and the wordmark in ink, for white surfaces. stacked puts the mark
 * above the wordmark, as in the primary logo.
 */
@Component({
  selector: 'df-logo',
  template: `
    <span class="flex shrink-0 items-center justify-center" [class]="markClass()">
      <svg viewBox="0 0 65 83" fill="none" stroke="currentColor" stroke-linecap="round" stroke-linejoin="round"
           [class]="tone() === 'light' ? 'h-[60%]' : 'h-full'" aria-hidden="true">
        <path d="M3.5 3.25V49.5M50.28 24.63A30 30 0 1 0 59.48 64.5M37.5 33.25H60L37.5 51.5H61.9" stroke-width="6" />
        <circle cx="22.75" cy="40.75" r="3.1" fill="currentColor" stroke="none" />
        <path d="M18.1 60.25Q32.7 76 47.25 60.25" stroke-width="2.4" />
      </svg>
    </span>
    @if (wordmark()) {
      <span class="flex flex-col" [class.items-center]="stacked()" aria-hidden="true">
        <span class="font-logo leading-none font-bold tracking-[-0.01em]" [class]="tone() === 'light' ? 'text-ink' : 'text-white'">hello<span
              [class]="tone() === 'light' ? 'text-ink' : 'text-accent'">Zen</span></span>
        @if (tagline()) {
          <span class="mt-[1.3em] text-[0.24em] leading-none font-semibold tracking-[0.42em] whitespace-nowrap uppercase"
                [class]="tone() === 'light' ? 'text-ink-muted' : 'text-white/80'">Pay · Split · Enjoy</span>
        }
      </span>
    }
  `,
  host: { class: 'inline-flex items-center', '[class.flex-col]': 'stacked()', dir: 'ltr', role: 'img', 'aria-label': 'helloZen' }
})
export class Logo {
  readonly tone = input<'dark' | 'light'>('dark');
  readonly wordmark = input(true);
  readonly tagline = input(false);
  readonly stacked = input(false);

  protected readonly markClass = computed(() => {
    const size = this.tone() === 'light' ? 'size-[1.75em] rounded-full bg-accent text-ink' : `${this.stacked() ? 'h-[2.8em]' : 'h-[1.6em]'} text-accent`;
    const gap = !this.wordmark() ? '' : this.stacked() ? 'mb-[0.35em]' : 'mr-[0.3em]';
    return `${size} ${gap}`;
  });
}
