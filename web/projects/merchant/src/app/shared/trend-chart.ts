import { Component, ElementRef, OnDestroy, OnInit, computed, inject, input, signal } from '@angular/core';

import { TrendGranularity } from '../core/api';
import { I18n } from '../core/i18n';
import { formatMoney, minorDigits } from '../core/money';
import { TREND_CHART } from './trend-chart.i18n';

export interface TrendValue {
  start: string;
  valueMinor: number;
}

/** Validated against the chart surface (lightness, chroma and 3:1 contrast). */
const BAR = '#0E9384';
const BAR_HOVER = '#0B7A6E';
const HEIGHT = 240;
const MARGIN = { top: 12, right: 8, bottom: 28, left: 56 };

/**
 * A single-series column chart over time (revenue or tips). Columns are at most 24px wide
 * with rounded tops; every column shows its period and value on hover and keyboard focus, and
 * the same numbers are available as a table.
 */
@Component({
  selector: 'df-trend-chart',
  template: `
    @let t = this.t();
    <div class="relative" (mouseleave)="hovered.set(null)">
      <svg [attr.width]="width()" [attr.height]="height" role="img" [attr.aria-label]="label()" direction="ltr" class="block">
        @for (tick of ticks(); track tick.value) {
          <line [attr.x1]="margin.left" [attr.x2]="width() - margin.right" [attr.y1]="tick.y" [attr.y2]="tick.y"
                [attr.stroke]="tick.value === 0 ? '#D0D5DD' : '#EAECF0'" stroke-width="1" shape-rendering="crispEdges" />
          <text [attr.x]="margin.left - 8" [attr.y]="tick.y + 4" text-anchor="end" class="fill-ink-muted text-[11px]">{{ tick.text }}</text>
        }
        @for (bar of bars(); track bar.start; let i = $index) {
          @if (bar.path) {
            <path [attr.d]="bar.path" [attr.fill]="hovered() === i ? barHover : barColor" />
          }
          @if (bar.showLabel) {
            <text [attr.x]="bar.cx" [attr.y]="height - 8" text-anchor="middle" class="fill-ink-muted text-[11px]">{{ bar.label }}</text>
          }
          <rect [attr.x]="bar.bandX" [attr.y]="margin.top" [attr.width]="bar.bandWidth" [attr.height]="plotHeight" fill="transparent"
                tabindex="0" class="cursor-default outline-none focus:stroke-ink focus:stroke-1"
                [attr.aria-label]="bar.label + ': ' + money(bar.value)"
                (mouseenter)="hovered.set(i)" (focus)="hovered.set(i)" (blur)="hovered.set(null)" />
        }
      </svg>
      @if (hovered() !== null && bars()[hovered()!]; as bar) {
        <div class="pointer-events-none absolute z-10 -translate-x-1/2 rounded-lg border border-line bg-white px-3 py-2 text-xs whitespace-nowrap shadow-md"
             [style.left.px]="bar.cx" [style.top.px]="Math.max(0, bar.top - 52)" role="status">
          <div class="text-sm font-semibold text-ink">{{ money(bar.value) }}</div>
          <div class="text-ink-muted">{{ bar.label }}</div>
        </div>
      }
    </div>
    <button type="button" class="link mt-2 text-sm" [attr.aria-expanded]="showTable()" (click)="showTable.set(!showTable())">
      {{ showTable() ? t.hideTable : t.showTable }}
    </button>
    @if (showTable()) {
      <table class="mt-2 w-full text-sm">
        <thead class="text-xs text-ink-muted uppercase"><tr><th scope="col" class="py-1 text-start font-semibold">{{ t.period }}</th><th scope="col" class="py-1 text-end font-semibold">{{ seriesName() }}</th></tr></thead>
        <tbody>
          @for (bar of bars(); track bar.start) {
            <tr class="border-t border-line"><td class="py-1.5">{{ bar.label }}</td><td class="py-1.5 text-end tabular-nums">{{ money(bar.value) }}</td></tr>
          }
        </tbody>
      </table>
    }
  `
})
export class TrendChart implements OnInit, OnDestroy {
  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly i18n = inject(I18n);
  protected readonly t = this.i18n.use(TREND_CHART);
  private observer?: ResizeObserver;

  readonly points = input.required<TrendValue[]>();
  readonly currency = input.required<string>();
  readonly granularity = input.required<TrendGranularity>();
  readonly timeZone = input.required<string>();
  readonly seriesName = input.required<string>();

  protected readonly Math = Math;
  protected readonly height = HEIGHT;
  protected readonly margin = MARGIN;
  protected readonly plotHeight = HEIGHT - MARGIN.top - MARGIN.bottom;
  protected readonly barColor = BAR;
  protected readonly barHover = BAR_HOVER;
  protected readonly width = signal(640);
  protected readonly hovered = signal<number | null>(null);
  protected readonly showTable = signal(false);

  protected readonly label = computed(() => this.t().by(this.seriesName(), this.granularity()));

  /** Round tick values: 0 and three or four steps of 1, 2 or 5 × a power of ten. */
  protected readonly ticks = computed(() => {
    const digits = minorDigits(this.currency());
    const max = Math.max(...this.points().map(p => p.valueMinor), 0) / 10 ** digits;
    const step = niceStep(max / 4 || 1);
    const top = Math.max(step, Math.ceil(max / step) * step);
    const compact = new Intl.NumberFormat(this.i18n.locale(), { notation: 'compact', maximumFractionDigits: 1 });
    const ticks = [];
    for (let v = 0; v <= top + step / 2; v += step) {
      ticks.push({ value: v, y: MARGIN.top + this.plotHeight * (1 - v / top), text: compact.format(v) });
    }
    return ticks;
  });

  protected readonly bars = computed(() => {
    const points = this.points();
    const digits = minorDigits(this.currency());
    const top = this.ticks().at(-1)!.value * 10 ** digits || 1;
    const plotWidth = Math.max(1, this.width() - MARGIN.left - MARGIN.right);
    const band = plotWidth / Math.max(1, points.length);
    const barWidth = Math.max(2, Math.min(24, band * 0.7));
    const every = Math.ceil(points.length / Math.max(1, Math.floor(plotWidth / 72)));
    const baseline = MARGIN.top + this.plotHeight;

    return points.map((p, i) => {
      const x = MARGIN.left + i * band + (band - barWidth) / 2;
      const h = (p.valueMinor / top) * this.plotHeight;
      const y = baseline - h;
      const r = Math.min(4, h, barWidth / 2);
      return {
        start: p.start,
        value: p.valueMinor,
        label: this.format(p.start),
        showLabel: i % every === 0,
        cx: x + barWidth / 2,
        top: y,
        bandX: MARGIN.left + i * band,
        bandWidth: band,
        // Rounded at the data end, square on the baseline.
        path: h <= 0 ? null
          : `M${x},${baseline}V${y + r}Q${x},${y} ${x + r},${y}H${x + barWidth - r}Q${x + barWidth},${y} ${x + barWidth},${y + r}V${baseline}Z`
      };
    });
  });

  ngOnInit(): void {
    const element = this.host.nativeElement as HTMLElement;
    this.width.set(element.clientWidth || 640);
    this.observer = new ResizeObserver(entries => this.width.set(Math.floor(entries[0]!.contentRect.width) || 640));
    this.observer.observe(element);
  }

  ngOnDestroy(): void {
    this.observer?.disconnect();
  }

  protected money(minor: number): string {
    return formatMoney(minor, this.currency());
  }

  /** The bucket's start in the venue's time zone: "14:00", "10 Mar" or "Mar 2027". */
  private format(iso: string): string {
    const date = new Date(iso);
    const timeZone = this.timeZone();
    const locale = this.i18n.locale();
    switch (this.granularity()) {
      case 'Hourly': return date.toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit', timeZone });
      case 'Monthly': return date.toLocaleDateString(locale, { month: 'short', year: 'numeric', timeZone });
      default: return date.toLocaleDateString(locale, { day: '2-digit', month: 'short', timeZone });
    }
  }
}

function niceStep(raw: number): number {
  const power = 10 ** Math.floor(Math.log10(raw));
  const fraction = raw / power;
  return (fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10) * power;
}
