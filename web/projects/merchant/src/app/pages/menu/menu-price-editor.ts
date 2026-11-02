import { Component, ElementRef, OnInit, afterNextRender, computed, inject, input, output, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { MenuDto, MenuItemDto } from '../../core/api';
import { I18n } from '../../core/i18n';
import { formatMoney, parseMajor, toMajorString } from '../../core/money';
import { MENU } from './menu.i18n';

/**
 * What one menu charges for one of its items. The item's own price, shown on the Items page and
 * used by every other menu, stays as it is.
 */
@Component({
  selector: 'hz-menu-price-editor',
  imports: [FormsModule],
  template: `
    @let t = this.t();
    <div class="modal-backdrop" (click)="cancelled.emit()"></div>
    <form class="modal max-w-md" role="dialog" aria-modal="true" aria-labelledby="menu-price-title" (ngSubmit)="save()" (keydown.escape)="cancelled.emit()">
      <h2 id="menu-price-title" class="text-lg font-bold">{{ t.admin.priceTitle(item().name, menu().name) }}</h2>

      <label class="mt-4 block">
        <span class="field-label">{{ t.admin.menuPrice(currency()) }}</span>
        <input #price class="field" name="price" inputmode="decimal" [(ngModel)]="model" required aria-describedby="menu-price-hint" />
      </label>
      <p id="menu-price-hint" class="mt-1 text-xs text-ink-muted">{{ t.admin.priceHint(money(itemPrice())) }}</p>

      @if (error()) { <p class="mt-4 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-800" role="alert">{{ error() }}</p> }

      <div class="mt-6 flex flex-wrap items-center justify-end gap-2">
        @if (ownPrice()) {
          <button type="button" class="btn-ghost me-auto" (click)="saved.emit(null)">{{ t.admin.useItemPrice }}</button>
        }
        <button type="button" class="btn-ghost" (click)="cancelled.emit()">{{ i18n.t().cancel }}</button>
        <button type="submit" class="btn-primary">{{ t.admin.savePrice }}</button>
      </div>
    </form>
  `
})
export class MenuPriceEditor implements OnInit {
  protected readonly i18n = inject(I18n);
  protected readonly t = this.i18n.use(MENU);

  readonly menu = input.required<MenuDto>();
  /** The item as this menu shows it. */
  readonly item = input.required<MenuItemDto>();
  readonly currency = input.required<string>();
  /** The menu's new price, or null to use the item's own price. */
  readonly saved = output<number | null>();
  readonly cancelled = output<void>();

  /** The menu sets its own price rather than using the item's. */
  protected readonly ownPrice = computed(() => this.item().itemPriceMinor != null);
  protected readonly itemPrice = computed(() => this.item().itemPriceMinor ?? this.item().priceMinor);
  protected readonly error = signal<string | null>(null);
  protected model = '';

  private readonly input = viewChild.required<ElementRef<HTMLInputElement>>('price');

  constructor() {
    // Once ngModel has written the price in, so typing replaces it.
    afterNextRender(() => setTimeout(() => this.input().nativeElement.select()));
  }

  ngOnInit(): void {
    this.model = toMajorString(this.item().priceMinor, this.currency());
  }

  protected money(minor: number): string {
    return formatMoney(minor, this.currency());
  }

  protected save(): void {
    const priceMinor = parseMajor(this.model, this.currency());
    if (priceMinor === null) return this.error.set(this.t().item.invalidPrice(this.currency()));
    // The item's own price needs no price of the menu's own.
    this.saved.emit(priceMinor === this.itemPrice() ? null : priceMinor);
  }

  /** Lets the page show a server-side validation message inside the dialog. */
  showError(message: string): void {
    this.error.set(message);
  }
}
