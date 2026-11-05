import { Component, DestroyRef, ElementRef, OnInit, afterRenderEffect, computed, inject, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';

import { Cart } from '../../core/cart';
import { GuestSession, errorCode } from '../../core/guest-session';
import { I18n } from '../../core/i18n';
import { Menu, MenuApi, MenuCategory, MenuItem, MenuTree, formatMoney } from '../../core/menu';
import { readJson, writeJson } from '../../core/storage';
import { ItemSheet } from './item-sheet';

type View = 'list' | 'grid';

/** A top-level category: its own items first, then each subcategory's under the subcategory's name. */
interface Section {
  id: string;
  title: string;
  groups: { id: string; title: string | null; items: MenuItem[] }[];
}

const VIEW_KEY = 'df.menuView';

/**
 * The e-menu (blueprint 4.2 steps 2–3, mockup "e-Menu home"): a header with the way back to the
 * table's home page, menu switcher, grid/list toggle, search, category chips (with arrows when they
 * run off the screen), the items category by category and a sticky order bar.
 */
@Component({
  selector: 'hz-menu',
  imports: [RouterLink, ItemSheet],
  templateUrl: './menu.html'
})
export class MenuPage implements OnInit {
  private readonly api = inject(MenuApi);
  private readonly session = inject(GuestSession);
  protected readonly cart = inject(Cart);
  protected readonly i18n = inject(I18n);

  protected readonly context = computed(() => this.session.current()?.context ?? null);
  /** The table's home page (the QR landing), which reopens the same session. */
  protected readonly home = computed(() => {
    const token = this.session.current()?.qrToken;
    return token ? ['/q', token] : ['/'];
  });
  protected readonly tree = signal<MenuTree | null>(null);
  protected readonly errorCode = signal<string | null>(null);

  protected readonly menuId = signal<string | null>(null);
  protected readonly view = signal<View>(readJson<View>('local', VIEW_KEY) ?? 'list');
  protected readonly search = signal('');
  protected readonly categoryId = signal<string | null>(null);
  protected readonly pickerOpen = signal(false);
  protected readonly openItem = signal<MenuItem | null>(null);
  protected readonly toast = signal<string | null>(null);
  /** Whether there are chips out of sight before or after the visible ones, so the arrows show. */
  protected readonly chipsBack = signal(false);
  protected readonly chipsForward = signal(false);
  private readonly chips = viewChild<ElementRef<HTMLElement>>('chips');

  protected readonly menu = computed<Menu | null>(() => {
    const menus = this.tree()?.menus ?? [];
    return menus.find(m => m.id === this.menuId()) ?? null;
  });

  /** Top-level categories of the current menu that have something to show. */
  protected readonly categories = computed(() =>
    (this.menu()?.categories ?? []).filter(c => allItems(c).length > 0)
  );

  protected readonly sections = computed<Section[]>(() => {
    const query = this.search().trim().toLocaleLowerCase();
    const selected = this.categoryId();
    const sections: Section[] = [];

    for (const category of this.categories()) {
      if (selected && category.id !== selected) continue;
      const groups = [category, ...category.subcategories]
        .map((part, i) => ({
          id: part.id,
          title: i === 0 ? null : this.i18n.localize(part.name, part.nameAr),
          items: part.items.filter(item => !query || this.matches(item, query))
        }))
        .filter(group => group.items.length > 0);
      if (groups.length) sections.push({ id: category.id, title: this.i18n.localize(category.name, category.nameAr), groups });
    }
    return sections;
  });

  protected readonly canOrder = computed(() => this.menu()?.availableNow ?? false);

  constructor() {
    // Recheck the arrows once the chips render or change, and when the row is resized (turning the phone, say).
    const resize = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(() => this.updateChipArrows());
    inject(DestroyRef).onDestroy(() => resize?.disconnect());
    afterRenderEffect(() => {
      const row = this.chips()?.nativeElement;
      this.categories();
      if (!row) return;
      resize?.observe(row);
      this.updateChipArrows();
    });
  }

  ngOnInit(): void {
    const context = this.context();
    if (!context) return;

    this.api.load(context.sessionId).subscribe({
      next: tree => {
        this.tree.set(tree);
        this.menuId.set((tree.menus.find(m => m.availableNow) ?? tree.menus[0])?.id ?? null);
      },
      error: error => this.errorCode.set(errorCode(error))
    });
  }

  protected updateChipArrows(): void {
    const row = this.chips()?.nativeElement;
    if (!row) return;
    // In Arabic the row scrolls the other way, so scrollLeft runs from 0 down to minus the overflow.
    const scrolled = Math.abs(row.scrollLeft);
    const overflow = row.scrollWidth - row.clientWidth;
    this.chipsBack.set(scrolled > 1);
    this.chipsForward.set(scrolled < overflow - 1);
  }

  /** Scrolls the chips most of the row's width on (1) or back (-1), in the reading direction. */
  protected scrollChips(step: 1 | -1): void {
    const row = this.chips()?.nativeElement;
    if (!row) return;
    const rtl = getComputedStyle(row).direction === 'rtl';
    row.scrollBy({ left: step * (rtl ? -1 : 1) * row.clientWidth * 0.7, behavior: 'smooth' });
  }

  protected selectMenu(menu: Menu): void {
    this.menuId.set(menu.id);
    this.categoryId.set(null);
    this.pickerOpen.set(false);
  }

  protected setView(view: View): void {
    this.view.set(view);
    writeJson('local', VIEW_KEY, view);
  }

  protected money(minor: number): string {
    return formatMoney(minor, this.tree()?.currency ?? 'AED', this.i18n.lang());
  }

  /** "until 11:30", "served 07:00–11:30" or "all day". */
  protected schedule(menu: Menu): string {
    const t = this.i18n.t();
    if (menu.availableNow) return menu.endTime ? t.until(menu.endTime) : t.allDay;
    return menu.startTime && menu.endTime ? t.servedBetween(menu.startTime, menu.endTime) : t.notServingNow;
  }

  protected orderable(item: MenuItem): boolean {
    return this.canOrder() && item.isAvailable;
  }

  protected isSimple(item: MenuItem): boolean {
    return item.modifierGroups.length === 0;
  }

  protected labels(item: MenuItem): string[] {
    const t = this.i18n.t();
    return [...item.allergens.map(a => t.contains(t.allergenNames[a] ?? a)), ...item.tags];
  }

  /** Quick add from the list: plain items go straight in, items with options open the sheet. */
  protected add(item: MenuItem): void {
    if (!this.isSimple(item)) {
      this.openItem.set(item);
      return;
    }
    this.cart.add({
      itemId: item.id, menuId: this.menuId(), name: item.name, nameAr: item.nameAr, imageUrl: item.imageUrl,
      unitPriceMinor: item.priceMinor, quantity: 1, options: [], note: ''
    });
  }

  protected removeOne(item: MenuItem): void {
    const line = this.cart.plainLine(item.id, this.menuId());
    if (line) this.cart.setQuantity(line.key, line.quantity - 1);
  }

  protected added(name: string): void {
    this.openItem.set(null);
    this.toast.set(this.i18n.t().added(name));
    setTimeout(() => this.toast.set(null), 2500);
  }

  /** First letters of the first two words, ignoring symbols such as "&". */
  protected initials(name: string): string {
    return name.split(/\s+/).map(w => w.match(/\p{L}/u)?.[0] ?? '').filter(Boolean).slice(0, 2).join('').toUpperCase();
  }

  private matches(item: MenuItem, query: string): boolean {
    return [item.name, item.nameAr, item.description, item.descriptionAr, ...item.tags]
      .some(text => text?.toLocaleLowerCase().includes(query));
  }
}

function allItems(category: MenuCategory): MenuItem[] {
  return [...category.items, ...category.subcategories.flatMap(s => s.items)];
}
