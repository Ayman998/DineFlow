import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { Cart } from '../../core/cart';
import { GuestSession, errorCode } from '../../core/guest-session';
import { I18n } from '../../core/i18n';
import { Menu, MenuApi, MenuCategory, MenuItem, MenuTree, formatMoney } from '../../core/menu';
import { readJson, writeJson } from '../../core/storage';
import { ItemSheet } from './item-sheet';

type View = 'list' | 'grid';

interface Section {
  id: string;
  title: string;
  items: MenuItem[];
}

const VIEW_KEY = 'df.menuView';

/**
 * The e-menu (blueprint 4.2 steps 2–3, mockup "e-Menu home"): venue header, menu switcher,
 * grid/list toggle, search, category chips, items and a sticky order bar.
 */
@Component({
  selector: 'df-menu',
  imports: [RouterLink, ItemSheet],
  templateUrl: './menu.html'
})
export class MenuPage implements OnInit {
  private readonly api = inject(MenuApi);
  private readonly session = inject(GuestSession);
  protected readonly cart = inject(Cart);
  protected readonly i18n = inject(I18n);

  protected readonly context = computed(() => this.session.current()?.context ?? null);
  protected readonly tree = signal<MenuTree | null>(null);
  protected readonly errorCode = signal<string | null>(null);

  protected readonly menuId = signal<string | null>(null);
  protected readonly view = signal<View>(readJson<View>('local', VIEW_KEY) ?? 'list');
  protected readonly search = signal('');
  protected readonly categoryId = signal<string | null>(null);
  protected readonly pickerOpen = signal(false);
  protected readonly openItem = signal<MenuItem | null>(null);
  protected readonly toast = signal<string | null>(null);

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
      for (const section of [category, ...category.subcategories]) {
        const items = section.items.filter(item => !query || this.matches(item, query));
        if (items.length) sections.push({ id: section.id, title: this.i18n.localize(section.name, section.nameAr), items });
      }
    }
    return sections;
  });

  protected readonly canOrder = computed(() => this.menu()?.availableNow ?? false);

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
      itemId: item.id, name: item.name, nameAr: item.nameAr,
      unitPriceMinor: item.priceMinor, quantity: 1, options: [], note: ''
    });
  }

  protected removeOne(item: MenuItem): void {
    const line = this.cart.plainLine(item.id);
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
