import { Component, OnInit, computed, inject, input, linkedSignal, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Observable } from 'rxjs';

import { CatalogDto, CategoryDto, ItemRequest, MenuAdminApi, MenuItemDto } from '../../core/api';
import { Auth, errorMessage } from '../../core/auth';
import { CurrentVenue } from '../../core/current-venue';
import { I18n } from '../../core/i18n';
import { formatMoney } from '../../core/money';
import { CategoryChoice, ItemEditor } from '../menu/item-editor';
import { MENU } from '../menu/menu.i18n';

/** A category's items as shown, under a heading such as "Mains › Grills". */
interface ItemGroup {
  category: CategoryDto;
  label: string;
  items: MenuItemDto[];
}

/**
 * The venue's items (blueprint 4.4 "Menu"; P1-MENU-02, 03, 06, 07): prices, photos, allergens,
 * tags, availability and modifiers, by category. Menus pick which ones they show.
 */
@Component({
  selector: 'df-items',
  imports: [FormsModule, RouterLink, ItemEditor],
  templateUrl: './items.html'
})
export class Items implements OnInit {
  private readonly api = inject(MenuAdminApi);
  private readonly currentVenue = inject(CurrentVenue);
  protected readonly auth = inject(Auth);
  protected readonly i18n = inject(I18n);
  protected readonly t = this.i18n.use(MENU);

  /** ?category= from the Item Categories page, bound through withComponentInputBinding(). */
  readonly category = input<string>();

  protected readonly catalog = signal<CatalogDto | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly dialog = signal<{ item: MenuItemDto | null } | null>(null);

  protected readonly query = signal('');
  protected readonly categoryFilter = linkedSignal(() => this.category() ?? '');
  protected readonly searching = computed(() => !!this.query().trim());

  private readonly editor = viewChild(ItemEditor);

  protected readonly currency = computed(() => this.catalog()?.currency ?? 'AED');

  /** Every category and subcategory, labelled "Mains › Grills", for the filter and the item's category picker. */
  protected readonly categoryChoices = computed<CategoryChoice[]>(() =>
    (this.catalog()?.categories ?? []).flatMap(c => [
      { id: c.id, label: c.name },
      ...c.subcategories.map(s => ({ id: s.id, label: `${c.name} › ${s.name}` }))
    ]));

  protected readonly itemTotal = computed(() =>
    (this.catalog()?.categories ?? []).reduce((n, c) => n + c.items.length + c.subcategories.reduce((m, s) => m + s.items.length, 0), 0));

  /** The names of the menus that show each item. */
  private readonly menusByItem = computed(() => {
    const result = new Map<string, string[]>();
    for (const menu of this.catalog()?.menus ?? []) {
      for (const id of menu.itemIds) result.set(id, [...(result.get(id) ?? []), menu.name]);
    }
    return result;
  });

  /** Categories with items matching the search; a top-level category filter includes its subcategories. */
  protected readonly groups = computed<ItemGroup[]>(() => {
    const q = this.query().trim().toLowerCase();
    const filter = this.categoryFilter();
    const matches = (i: MenuItemDto) => !q || i.name.toLowerCase().includes(q) || (i.nameAr ?? '').includes(q);

    return (this.catalog()?.categories ?? [])
      .flatMap(c => [
        { category: c, label: c.name, parentId: null as string | null },
        ...c.subcategories.map(s => ({ category: s, label: `${c.name} › ${s.name}`, parentId: c.id as string | null }))
      ])
      .filter(g => !filter || g.category.id === filter || g.parentId === filter)
      .map(g => ({ category: g.category, label: g.label, items: g.category.items.filter(matches) }))
      .filter(g => g.items.length);
  });

  ngOnInit(): void {
    this.currentVenue.load().subscribe({
      next: venue => (venue ? this.refresh() : this.fail(this.i18n.t().noVenue)),
      error: error => this.fail(errorMessage(error))
    });
  }

  protected money(minor: number): string {
    return formatMoney(minor, this.currency());
  }

  protected menusOf(item: MenuItemDto): string[] {
    return this.menusByItem().get(item.id) ?? [];
  }

  /** A new item starts in the filtered category, else the first one. */
  protected newItemCategory(): string {
    return this.categoryFilter() || this.categoryChoices()[0]?.id || '';
  }

  protected save(request: ItemRequest): void {
    const editing = this.dialog()?.item;
    const call = editing ? this.api.updateItem(editing.id, request) : this.api.createItem(request);
    this.submit(call, saved => {
      this.dialog.set(null);
      this.notice.set(this.t().saved(saved.name));
    }, message => this.editor()?.showError(message));
  }

  protected toggleAvailability(item: MenuItemDto): void {
    this.submit(this.api.setAvailability(item.id, !item.isAvailable), saved =>
      this.notice.set(this.t().availabilityChanged(saved.name, saved.isAvailable)));
  }

  protected delete(item: MenuItemDto): void {
    if (!confirm(this.t().itemsPage.confirmDelete(item.name))) return;
    this.submit(this.api.deleteItem(item.id), () => this.notice.set(this.t().deleted(item.name)));
  }

  protected move(item: MenuItemDto, siblings: MenuItemDto[], step: -1 | 1): void {
    const ids = siblings.map(s => s.id);
    const from = ids.indexOf(item.id);
    const to = from + step;
    if (from < 0 || to < 0 || to >= ids.length) return;
    [ids[from], ids[to]] = [ids[to]!, ids[from]!];
    this.submit(this.api.reorder('items', ids), () => undefined);
  }

  /**
   * Runs a change, then reloads the catalog. Errors go to `showInDialog` when the
   * change came from the item drawer, otherwise to the page.
   */
  private submit<T>(call: Observable<T>, done: (result: T) => void, showInDialog?: (message: string) => void): void {
    this.error.set(null);
    this.notice.set(null);
    call.subscribe({
      next: result => {
        done(result);
        this.refresh();
      },
      error: error => (showInDialog ?? (message => this.error.set(message)))(errorMessage(error))
    });
  }

  private refresh(): void {
    const venue = this.currentVenue.venue();
    if (!venue) return;
    this.api.catalog(venue.id).subscribe({
      next: catalog => {
        this.catalog.set(catalog);
        if (!this.categoryChoices().some(c => c.id === this.categoryFilter())) this.categoryFilter.set('');
        this.loading.set(false);
      },
      error: error => this.fail(errorMessage(error))
    });
  }

  private fail(message: string): void {
    this.error.set(message);
    this.loading.set(false);
  }
}
