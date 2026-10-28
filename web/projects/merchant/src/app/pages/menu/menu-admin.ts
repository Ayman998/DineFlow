import { NgTemplateOutlet } from '@angular/common';
import { Component, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { Observable } from 'rxjs';

import { CategoryDto, ItemRequest, MenuAdminApi, MenuDto, MenuItemDto, MenuRequest, MenuTreeDto } from '../../core/api';
import { Auth, errorMessage } from '../../core/auth';
import { CurrentVenue } from '../../core/current-venue';
import { I18n } from '../../core/i18n';
import { formatMoney } from '../../core/money';
import { CategoryEditor, CategoryNames } from './category-editor';
import { CategoryChoice, ItemEditor } from './item-editor';
import { MenuEditor } from './menu-editor';
import { MENU } from './menu.i18n';

type CategoryDialog = { menuId: string; parentId: string | null; category: CategoryDto | null };
type ItemDialog = { categoryId: string; item: MenuItemDto | null };

/**
 * Menu management (blueprint 4.4 "Menu"; P1-MENU-01..07): menus and their schedules,
 * categories and subcategories, items with prices, availability, allergens, tags and modifiers.
 */
@Component({
  selector: 'df-menu-admin',
  imports: [NgTemplateOutlet, MenuEditor, CategoryEditor, ItemEditor],
  templateUrl: './menu-admin.html'
})
export class MenuAdmin implements OnInit {
  private readonly api = inject(MenuAdminApi);
  private readonly currentVenue = inject(CurrentVenue);
  protected readonly auth = inject(Auth);
  protected readonly i18n = inject(I18n);
  protected readonly t = this.i18n.use(MENU);

  protected readonly tree = signal<MenuTreeDto | null>(null);
  protected readonly menuId = signal<string | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);

  protected readonly menuDialog = signal<{ menu: MenuDto | null } | null>(null);
  protected readonly categoryDialog = signal<CategoryDialog | null>(null);
  protected readonly itemDialog = signal<ItemDialog | null>(null);

  private readonly menuEditor = viewChild(MenuEditor);
  private readonly categoryEditor = viewChild(CategoryEditor);
  private readonly itemEditor = viewChild(ItemEditor);

  protected readonly menu = computed(() => this.tree()?.menus.find(m => m.id === this.menuId()) ?? null);
  protected readonly menuIndex = computed(() => this.tree()?.menus.findIndex(m => m.id === this.menuId()) ?? -1);
  protected readonly currency = computed(() => this.tree()?.currency ?? 'AED');

  /** Every category of the current menu, labelled "Mains › Grills", for the item's category picker. */
  protected readonly categoryChoices = computed<CategoryChoice[]>(() =>
    (this.menu()?.categories ?? []).flatMap(c => [
      { id: c.id, label: c.name },
      ...c.subcategories.map(s => ({ id: s.id, label: `${c.name} › ${s.name}` }))
    ]));

  protected readonly itemCount = computed(() =>
    (this.menu()?.categories ?? []).reduce((n, c) => n + c.items.length + c.subcategories.reduce((m, s) => m + s.items.length, 0), 0));

  ngOnInit(): void {
    this.currentVenue.load().subscribe({
      next: venue => (venue ? this.refresh() : this.fail(this.i18n.t().noVenue)),
      error: error => this.fail(errorMessage(error))
    });
  }

  protected money(minor: number): string {
    return formatMoney(minor, this.currency());
  }

  protected schedule(menu: MenuDto): string {
    const t = this.t();
    const days = menu.days.length === 7 ? t.admin.everyDay : menu.days.map(d => t.days[d] ?? d.slice(0, 3)).join(t.admin.listSeparator);
    return menu.startTime ? t.admin.hours(days, menu.startTime, menu.endTime ?? '') : t.admin.allDay(days);
  }

  // ---- Menus ----

  protected saveMenu(request: MenuRequest): void {
    const venue = this.currentVenue.venue();
    const editing = this.menuDialog()?.menu;
    if (!venue) return;

    const call = editing ? this.api.updateMenu(editing.id, request) : this.api.createMenu(venue.id, request);
    this.submit(call, saved => {
      this.menuDialog.set(null);
      this.menuId.set(saved.id);
      this.notice.set(this.t().admin.savedMenu(saved.name));
    }, message => this.menuEditor()?.showError(message));
  }

  protected deleteMenu(menu: MenuDto): void {
    if (!confirm(this.t().admin.confirmDeleteMenu(menu.name))) return;
    this.submit(this.api.deleteMenu(menu.id), () => {
      this.menuId.set(null);
      this.notice.set(this.t().admin.deletedMenu(menu.name));
    });
  }

  protected moveMenu(menu: MenuDto, step: -1 | 1): void {
    this.move('menus', this.tree()?.menus ?? [], menu, step);
  }

  // ---- Categories ----

  protected saveCategory(names: CategoryNames): void {
    const dialog = this.categoryDialog();
    if (!dialog) return;

    const call = dialog.category
      ? this.api.updateCategory(dialog.category.id, names.name, names.nameAr)
      : this.api.createCategory(dialog.menuId, dialog.parentId, names.name, names.nameAr);
    this.submit(call, saved => {
      this.categoryDialog.set(null);
      this.notice.set(this.t().admin.savedCategory(saved.name));
    }, message => this.categoryEditor()?.showError(message));
  }

  protected deleteCategory(category: CategoryDto): void {
    const count = category.items.length + category.subcategories.reduce((n, s) => n + s.items.length, 0);
    if (!confirm(this.t().admin.confirmDeleteCategory(category.name, count))) return;
    this.submit(this.api.deleteCategory(category.id), () => this.notice.set(this.t().admin.deleted(category.name)));
  }

  protected moveCategory(category: CategoryDto, siblings: CategoryDto[], step: -1 | 1): void {
    this.move('categories', siblings, category, step);
  }

  // ---- Items ----

  protected saveItem(request: ItemRequest): void {
    const editing = this.itemDialog()?.item;
    const call = editing ? this.api.updateItem(editing.id, request) : this.api.createItem(request);
    this.submit(call, saved => {
      this.itemDialog.set(null);
      this.notice.set(this.t().admin.saved(saved.name));
    }, message => this.itemEditor()?.showError(message));
  }

  protected toggleAvailability(item: MenuItemDto): void {
    this.submit(this.api.setAvailability(item.id, !item.isAvailable), saved =>
      this.notice.set(this.t().admin.availabilityChanged(saved.name, saved.isAvailable)));
  }

  protected deleteItem(item: MenuItemDto): void {
    if (!confirm(this.t().admin.confirmDeleteItem(item.name))) return;
    this.submit(this.api.deleteItem(item.id), () => this.notice.set(this.t().admin.deleted(item.name)));
  }

  protected moveItem(item: MenuItemDto, siblings: MenuItemDto[], step: -1 | 1): void {
    this.move('items', siblings, item, step);
  }

  // ---- Helpers ----

  private move<T extends { id: string }>(kind: 'menus' | 'categories' | 'items', siblings: T[], entry: T, step: -1 | 1): void {
    const ids = siblings.map(s => s.id);
    const from = ids.indexOf(entry.id);
    const to = from + step;
    if (from < 0 || to < 0 || to >= ids.length) return;
    [ids[from], ids[to]] = [ids[to]!, ids[from]!];
    this.submit(this.api.reorder(kind, ids), () => undefined);
  }

  /**
   * Runs a change, then reloads the tree. Errors go to `showInDialog` when the
   * change came from a dialog, otherwise to the page.
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
    this.api.tree(venue.id).subscribe({
      next: tree => {
        this.tree.set(tree);
        if (!tree.menus.some(m => m.id === this.menuId())) this.menuId.set(tree.menus[0]?.id ?? null);
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
