import { NgTemplateOutlet } from '@angular/common';
import { Component, OnInit, inject, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Observable } from 'rxjs';

import { CatalogDto, CategoryDto, MenuAdminApi } from '../../core/api';
import { Auth, errorMessage } from '../../core/auth';
import { CurrentVenue } from '../../core/current-venue';
import { I18n } from '../../core/i18n';
import { CategoryEditor, CategoryNames } from '../menu/category-editor';
import { MENU } from '../menu/menu.i18n';

type CategoryDialog = { parentId: string | null; category: CategoryDto | null };

/**
 * The venue's item categories and subcategories (blueprint 4.4 "Menu"). Items are filed under
 * them on the Items page, and a menu shows a category when it links any of its items.
 */
@Component({
  selector: 'df-item-categories',
  imports: [NgTemplateOutlet, RouterLink, CategoryEditor],
  templateUrl: './item-categories.html'
})
export class ItemCategories implements OnInit {
  private readonly api = inject(MenuAdminApi);
  private readonly currentVenue = inject(CurrentVenue);
  protected readonly auth = inject(Auth);
  protected readonly i18n = inject(I18n);
  protected readonly t = this.i18n.use(MENU);

  protected readonly catalog = signal<CatalogDto | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);
  protected readonly dialog = signal<CategoryDialog | null>(null);

  private readonly editor = viewChild(CategoryEditor);

  ngOnInit(): void {
    this.currentVenue.load().subscribe({
      next: venue => (venue ? this.refresh() : this.fail(this.i18n.t().noVenue)),
      error: error => this.fail(errorMessage(error))
    });
  }

  /** Items in the category, including its subcategories'. */
  protected itemTotal(category: CategoryDto): number {
    return category.items.length + category.subcategories.reduce((n, s) => n + s.items.length, 0);
  }

  protected save(names: CategoryNames): void {
    const dialog = this.dialog();
    const venue = this.currentVenue.venue();
    if (!dialog || !venue) return;

    const call = dialog.category
      ? this.api.updateCategory(dialog.category.id, names.name, names.nameAr)
      : this.api.createCategory(venue.id, dialog.parentId, names.name, names.nameAr);
    this.submit(call, saved => {
      this.dialog.set(null);
      this.notice.set(this.t().categoriesPage.savedCategory(saved.name));
    }, message => this.editor()?.showError(message));
  }

  protected delete(category: CategoryDto): void {
    if (!confirm(this.t().categoriesPage.confirmDelete(category.name, this.itemTotal(category)))) return;
    this.submit(this.api.deleteCategory(category.id), () => this.notice.set(this.t().deleted(category.name)));
  }

  protected move(category: CategoryDto, siblings: CategoryDto[], step: -1 | 1): void {
    const ids = siblings.map(s => s.id);
    const from = ids.indexOf(category.id);
    const to = from + step;
    if (from < 0 || to < 0 || to >= ids.length) return;
    [ids[from], ids[to]] = [ids[to]!, ids[from]!];
    this.submit(this.api.reorder('categories', ids), () => undefined);
  }

  /**
   * Runs a change, then reloads the catalog. Errors go to `showInDialog` when the
   * change came from the dialog, otherwise to the page.
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
