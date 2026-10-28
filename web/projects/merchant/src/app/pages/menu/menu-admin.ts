import { NgTemplateOutlet } from '@angular/common';
import { Component, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { Observable } from 'rxjs';

import { CatalogDto, MenuAdminApi, MenuDto, MenuItemDto, MenuRequest, MenuTreeDto } from '../../core/api';
import { Auth, errorMessage } from '../../core/auth';
import { CurrentVenue } from '../../core/current-venue';
import { I18n } from '../../core/i18n';
import { formatMoney } from '../../core/money';
import { LinkItems } from './link-items';
import { MenuEditor } from './menu-editor';
import { MENU } from './menu.i18n';

/**
 * Menu management (blueprint 4.4 "Menu"; P1-MENU-01..07): menus and their schedules, and which
 * items each one shows. Items and their categories are managed on their own pages and linked here.
 */
@Component({
  selector: 'df-menu-admin',
  imports: [NgTemplateOutlet, MenuEditor, LinkItems],
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
  protected readonly linkDialog = signal<{ menu: MenuDto; catalog: CatalogDto } | null>(null);

  private readonly menuEditor = viewChild(MenuEditor);
  private readonly linkItems = viewChild(LinkItems);

  protected readonly menu = computed(() => this.tree()?.menus.find(m => m.id === this.menuId()) ?? null);
  protected readonly menuIndex = computed(() => this.tree()?.menus.findIndex(m => m.id === this.menuId()) ?? -1);
  protected readonly currency = computed(() => this.tree()?.currency ?? 'AED');

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
    const ids = (this.tree()?.menus ?? []).map(m => m.id);
    const from = ids.indexOf(menu.id);
    const to = from + step;
    if (from < 0 || to < 0 || to >= ids.length) return;
    [ids[from], ids[to]] = [ids[to]!, ids[from]!];
    this.submit(this.api.reorder('menus', ids), () => undefined);
  }

  // ---- Linked items ----

  /** Opens the link dialog with the venue's whole catalog. */
  protected openLinks(menu: MenuDto): void {
    const venue = this.currentVenue.venue();
    if (!venue) return;
    this.error.set(null);
    this.api.catalog(venue.id).subscribe({
      next: catalog => this.linkDialog.set({ menu, catalog }),
      error: error => this.error.set(errorMessage(error))
    });
  }

  protected saveLinks(itemIds: string[]): void {
    const menu = this.linkDialog()?.menu;
    if (!menu) return;
    this.submit(this.api.setMenuItems(menu.id, itemIds), () => {
      this.linkDialog.set(null);
      this.notice.set(this.t().admin.linked(menu.name, itemIds.length));
    }, message => this.linkItems()?.showError(message));
  }

  protected toggleAvailability(item: MenuItemDto): void {
    this.submit(this.api.setAvailability(item.id, !item.isAvailable), saved =>
      this.notice.set(this.t().availabilityChanged(saved.name, saved.isAvailable)));
  }

  // ---- Helpers ----

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
