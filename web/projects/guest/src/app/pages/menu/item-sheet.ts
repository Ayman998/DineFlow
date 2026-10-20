import { Component, DOCUMENT, OnDestroy, OnInit, computed, inject, input, output, signal } from '@angular/core';

import { Cart, CartOption, MAX_QUANTITY } from '../../core/cart';
import { I18n } from '../../core/i18n';
import { MenuItem, ModifierGroup, ModifierOption, formatMoney } from '../../core/menu';

/**
 * Item detail and customisation (blueprint 4.2 step 4, mockup "Item customization"): photo,
 * description, allergens and tags, modifier groups with required/optional rules, a note,
 * quantity and the price updating as options change.
 */
@Component({
  selector: 'df-item-sheet',
  templateUrl: './item-sheet.html',
  host: { '(document:keydown.escape)': 'closed.emit()' }
})
export class ItemSheet implements OnInit, OnDestroy {
  private readonly cart = inject(Cart);
  private readonly document = inject(DOCUMENT);
  protected readonly i18n = inject(I18n);

  readonly item = input.required<MenuItem>();
  readonly currency = input.required<string>();
  /** False when the selected menu isn't being served right now. */
  readonly canOrder = input.required<boolean>();
  readonly menuSchedule = input('');

  readonly closed = output<void>();
  /** Emits the item's display name after it is added to the order. */
  readonly added = output<string>();

  protected readonly maxQuantity = MAX_QUANTITY;
  protected readonly selected = signal<ReadonlyMap<string, ReadonlySet<string>>>(new Map());
  protected readonly quantity = signal(1);
  protected readonly note = signal('');

  protected readonly name = computed(() => this.i18n.localize(this.item().name, this.item().nameAr));

  protected readonly unitPrice = computed(() => {
    const picked = this.selected();
    return this.item().modifierGroups.reduce(
      (sum, group) => sum + group.options.filter(o => picked.get(group.id)?.has(o.id)).reduce((s, o) => s + o.priceDeltaMinor, 0),
      this.item().priceMinor);
  });

  /** The first required group that still needs a choice, if any. */
  protected readonly missingGroup = computed(() =>
    this.item().modifierGroups.find(g => (this.selected().get(g.id)?.size ?? 0) < g.minSelect) ?? null);

  protected readonly canAdd = computed(() => this.canOrder() && this.item().isAvailable && !this.missingGroup());

  private previousOverflow = '';

  ngOnInit(): void {
    // Start from the merchant's defaults, skipping anything sold out.
    this.selected.set(new Map(this.item().modifierGroups.map(g => [
      g.id, new Set(g.options.filter(o => o.isDefault && o.isAvailable).slice(0, g.maxSelect).map(o => o.id))
    ])));

    this.previousOverflow = this.document.body.style.overflow;
    this.document.body.style.overflow = 'hidden';
  }

  ngOnDestroy(): void {
    this.document.body.style.overflow = this.previousOverflow;
  }

  protected money(minor: number): string {
    return formatMoney(minor, this.currency(), this.i18n.lang());
  }

  protected isSingle(group: ModifierGroup): boolean {
    return group.maxSelect === 1;
  }

  protected isChecked(group: ModifierGroup, option: ModifierOption): boolean {
    return this.selected().get(group.id)?.has(option.id) ?? false;
  }

  /** Sold out, or the group already has as many picks as it allows. */
  protected isDisabled(group: ModifierGroup, option: ModifierOption): boolean {
    if (!option.isAvailable) return true;
    if (this.isSingle(group) || this.isChecked(group, option)) return false;
    return (this.selected().get(group.id)?.size ?? 0) >= group.maxSelect;
  }

  protected toggle(group: ModifierGroup, option: ModifierOption): void {
    this.selected.update(current => {
      const next = new Map(current);
      const picks = new Set(next.get(group.id) ?? []);
      if (this.isSingle(group)) {
        // Radio behaviour; an optional single choice can be cleared by tapping it again.
        const wasChecked = picks.has(option.id);
        picks.clear();
        if (!wasChecked || group.minSelect > 0) picks.add(option.id);
      } else if (picks.has(option.id)) {
        picks.delete(option.id);
      } else if (picks.size < group.maxSelect) {
        picks.add(option.id);
      }
      next.set(group.id, picks);
      return next;
    });
  }

  protected groupBadge(group: ModifierGroup): string {
    const t = this.i18n.t();
    return group.minSelect > 0 ? `${t.required} · ${t.pick(group.minSelect, group.maxSelect)}` : group.maxSelect > 1 ? `${t.optional} · ${t.pick(0, group.maxSelect)}` : t.optional;
  }

  protected setQuantity(quantity: number): void {
    this.quantity.set(Math.max(1, Math.min(MAX_QUANTITY, quantity)));
  }

  protected addToOrder(): void {
    if (!this.canAdd()) return;

    const item = this.item();
    const picked = this.selected();
    const options: CartOption[] = item.modifierGroups.flatMap(g =>
      g.options.filter(o => picked.get(g.id)?.has(o.id)).map(o => ({
        groupId: g.id, optionId: o.id, name: o.name, nameAr: o.nameAr, priceDeltaMinor: o.priceDeltaMinor
      })));

    this.cart.add({
      itemId: item.id, name: item.name, nameAr: item.nameAr,
      unitPriceMinor: this.unitPrice(), quantity: this.quantity(), options, note: this.note().trim()
    });
    this.added.emit(this.name());
  }
}
