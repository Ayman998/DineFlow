import { Component, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { CatalogDto, CategoryDto, MenuItemDto } from '../../core/api';
import { I18n } from '../../core/i18n';
import { formatMoney } from '../../core/money';
import { MENU } from '../menu/menu.i18n';

/** Another category's items, under a heading such as "Mains › Grills". */
interface Group {
  category: CategoryDto;
  label: string;
  items: MenuItemDto[];
}

/**
 * Picks items from the venue's other categories to move into one category. An item is in one
 * category at a time, so moving it takes it out of its old one; it keeps its menus and prices.
 * Ticking a heading takes every item under it that matches the search.
 */
@Component({
  selector: 'hz-link-category-items',
  imports: [FormsModule],
  template: `
    @let t = this.t();
    @let c = t.categoriesPage;
    <div class="modal-backdrop" (click)="cancelled.emit()"></div>
    <form class="modal flex max-w-2xl flex-col" role="dialog" aria-modal="true" aria-labelledby="link-category-title" (ngSubmit)="save()">
      <h2 id="link-category-title" class="text-lg font-bold">{{ c.linkTitle(category().name) }}</h2>
      <p class="mt-1 text-sm text-ink-muted">
        {{ c.linkIntro }} @if (here()) { <span class="whitespace-nowrap">({{ c.alreadyHere }}: {{ t.itemCount(here()) }})</span> }
      </p>

      @if (!others()) {
        <div class="mt-6 rounded-lg bg-surface p-6 text-center text-sm">
          <p class="font-semibold">{{ t.link.noItems }}</p>
          <p class="mt-1 text-ink-muted">{{ t.link.noItemsHint }}</p>
        </div>
      } @else {
        <input class="field mt-4" type="search" name="query" [ngModel]="query()" (ngModelChange)="query.set($event)"
               [placeholder]="t.link.search" [attr.aria-label]="t.link.search" />

        <div class="mt-3 min-h-0 flex-1 overflow-y-auto rounded-lg border border-line">
          @for (group of groups(); track group.category.id) {
            @let all = isAll(group);
            <label class="flex cursor-pointer items-center gap-3 border-t border-line bg-surface px-4 py-2 text-sm font-semibold first:border-t-0">
              <input type="checkbox" class="size-4 accent-brand" [checked]="all" [indeterminate]="!all && isSome(group)"
                     [attr.aria-label]="t.link.allIn(group.label)" (change)="toggleGroup(group)" />
              <span class="flex-1">
                {{ group.label }}
                @if (group.category.nameAr) { <span class="ms-1 font-normal text-ink-muted" dir="rtl">{{ group.category.nameAr }}</span> }
              </span>
            </label>
            @for (item of group.items; track item.id) {
              <label class="flex cursor-pointer items-center gap-3 border-t border-line px-4 py-2 ps-10 text-sm hover:bg-surface/60"
                     [class.opacity-60]="!item.isAvailable">
                <input type="checkbox" class="size-4 accent-brand" [checked]="selected().has(item.id)" [attr.aria-label]="item.name" (change)="toggle(item.id)" />
                <span class="flex-1">
                  {{ item.name }}
                  @if (item.nameAr) { <span class="ms-1 text-ink-muted" dir="rtl">{{ item.nameAr }}</span> }
                  @if (!item.isAvailable) { <span class="ms-1 text-xs text-red-700">{{ t.soldOut }}</span> }
                </span>
                <span class="text-ink-muted tabular-nums">{{ money(item.priceMinor) }}</span>
              </label>
            }
          } @empty {
            <p class="p-6 text-center text-sm text-ink-muted">{{ t.link.noMatches }}</p>
          }
        </div>
      }

      @if (error()) { <p class="mt-4 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-800" role="alert">{{ error() }}</p> }
      <div class="mt-6 flex flex-wrap items-center justify-between gap-2">
        <span class="text-sm text-ink-muted" role="status">{{ c.toMove(selected().size) }}</span>
        <div class="flex gap-2">
          <button type="button" class="btn-ghost" (click)="cancelled.emit()">{{ i18n.t().cancel }}</button>
          <button type="submit" class="btn-primary" [disabled]="!selected().size">{{ i18n.t().save }}</button>
        </div>
      </div>
    </form>
  `
})
export class LinkCategoryItems {
  protected readonly i18n = inject(I18n);
  protected readonly t = this.i18n.use(MENU);

  readonly category = input.required<CategoryDto>();
  readonly catalog = input.required<CatalogDto>();
  readonly saved = output<string[]>();
  readonly cancelled = output<void>();

  protected readonly query = signal('');
  protected readonly selected = signal(new Set<string>());
  protected readonly error = signal<string | null>(null);

  /** Every category and subcategory except this one, each with its items. */
  private readonly allGroups = computed<Group[]>(() =>
    this.catalog().categories
      .flatMap(c => [
        { category: c, label: c.name, items: c.items },
        ...c.subcategories.map(s => ({ category: s, label: `${c.name} › ${s.name}`, items: s.items }))
      ])
      .filter(g => g.category.id !== this.category().id));

  /** Items already in this category. */
  protected readonly here = computed(() => this.catalog().categories
    .flatMap(c => [c, ...c.subcategories])
    .find(c => c.id === this.category().id)?.items.length ?? 0);

  protected readonly others = computed(() => this.allGroups().reduce((n, g) => n + g.items.length, 0));

  /** Categories with items matching the search. */
  protected readonly groups = computed<Group[]>(() => {
    const q = this.query().trim().toLowerCase();
    const matches = (i: MenuItemDto) => !q || i.name.toLowerCase().includes(q) || (i.nameAr ?? '').includes(q);
    return this.allGroups().map(g => ({ ...g, items: g.items.filter(matches) })).filter(g => g.items.length);
  });

  protected isAll(group: Group): boolean {
    const selected = this.selected();
    return group.items.every(i => selected.has(i.id));
  }

  protected isSome(group: Group): boolean {
    const selected = this.selected();
    return group.items.some(i => selected.has(i.id));
  }

  protected toggle(id: string): void {
    this.selected.update(set => {
      const next = new Set(set);
      if (!next.delete(id)) next.add(id);
      return next;
    });
  }

  protected toggleGroup(group: Group): void {
    const all = this.isAll(group);
    this.selected.update(set => {
      const next = new Set(set);
      for (const item of group.items) {
        if (all) next.delete(item.id);
        else next.add(item.id);
      }
      return next;
    });
  }

  protected money(minor: number): string {
    return formatMoney(minor, this.catalog().currency);
  }

  protected save(): void {
    this.error.set(null);
    // In catalog order, so they line up after this category's own items as they were listed.
    const selected = this.selected();
    this.saved.emit(this.allGroups().flatMap(g => g.items).map(i => i.id).filter(id => selected.has(id)));
  }

  showError(message: string): void {
    this.error.set(message);
  }
}
