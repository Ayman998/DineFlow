import { Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';

import { CatalogDto, CategoryDto, MenuDto, MenuItemDto } from '../../core/api';
import { I18n } from '../../core/i18n';
import { formatMoney } from '../../core/money';
import { MENU } from './menu.i18n';

/** A category heading in the list: a top-level category covers its subcategories' items too. */
interface Group {
  category: CategoryDto;
  label: string;
  sub: boolean;
  items: MenuItemDto[];
  /** The visible items the heading's checkbox ticks and unticks. */
  allIds: string[];
}

/**
 * Picks which catalog items a menu shows. Ticking a category ticks every item in it and its
 * subcategories; a search narrows both the list and what a category tick covers.
 */
@Component({
  selector: 'df-link-items',
  imports: [FormsModule, RouterLink],
  template: `
    @let t = this.t();
    <div class="modal-backdrop" (click)="cancelled.emit()"></div>
    <form class="modal flex max-w-2xl flex-col" role="dialog" aria-modal="true" aria-labelledby="link-items-title" (ngSubmit)="save()">
      <h2 id="link-items-title" class="text-lg font-bold">{{ t.link.title(menu().name) }}</h2>
      <p class="mt-1 text-sm text-ink-muted">{{ t.link.intro }}</p>

      @if (!total()) {
        <div class="mt-6 rounded-lg bg-surface p-6 text-center text-sm">
          <p class="font-semibold">{{ t.link.noItems }}</p>
          <p class="mt-1 text-ink-muted">{{ t.link.noItemsHint }}</p>
          <a routerLink="/items" class="link mt-3 inline-block" (click)="cancelled.emit()">{{ i18n.t().nav['items'] }}</a>
        </div>
      } @else {
        <input class="field mt-4" type="search" name="query" [ngModel]="query()" (ngModelChange)="query.set($event)"
               [placeholder]="t.link.search" [attr.aria-label]="t.link.search" />

        <div class="mt-3 min-h-0 flex-1 overflow-y-auto rounded-lg border border-line">
          @for (group of groups(); track group.category.id) {
            @let all = isAll(group);
            <label class="flex cursor-pointer items-center gap-3 border-t border-line bg-surface px-4 py-2 text-sm font-semibold first:border-t-0"
                   [class.ps-10]="group.sub">
              <input type="checkbox" class="size-4 accent-brand" [checked]="all" [indeterminate]="!all && isSome(group)"
                     [attr.aria-label]="t.link.allIn(group.label)" (change)="toggleGroup(group)" />
              <span class="flex-1">
                {{ group.label }}
                @if (group.category.nameAr) { <span class="ms-1 font-normal text-ink-muted" dir="rtl">{{ group.category.nameAr }}</span> }
              </span>
              <span class="text-xs font-normal text-ink-muted tabular-nums">{{ countIn(group) }} / {{ group.allIds.length }}</span>
            </label>
            @for (item of group.items; track item.id) {
              <label class="flex cursor-pointer items-center gap-3 border-t border-line px-4 py-2 text-sm hover:bg-surface/60"
                     [class.ps-10]="!group.sub" [class.ps-16]="group.sub" [class.opacity-60]="!item.isAvailable">
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
        <span class="text-sm text-ink-muted" role="status">{{ t.link.selected(selected().size) }}</span>
        <div class="flex gap-2">
          <button type="button" class="btn-ghost" (click)="cancelled.emit()">{{ i18n.t().cancel }}</button>
          <button type="submit" class="btn-primary" [disabled]="!total()">{{ i18n.t().save }}</button>
        </div>
      </div>
    </form>
  `
})
export class LinkItems implements OnInit {
  protected readonly i18n = inject(I18n);
  protected readonly t = this.i18n.use(MENU);

  readonly menu = input.required<MenuDto>();
  readonly catalog = input.required<CatalogDto>();
  readonly saved = output<string[]>();
  readonly cancelled = output<void>();

  protected readonly query = signal('');
  protected readonly selected = signal(new Set<string>());
  protected readonly error = signal<string | null>(null);

  protected readonly total = computed(() =>
    this.catalog().categories.reduce((n, c) => n + c.items.length + c.subcategories.reduce((m, s) => m + s.items.length, 0), 0));

  /** Categories with matching items, each top-level one followed by its subcategories. */
  protected readonly groups = computed<Group[]>(() => {
    const q = this.query().trim().toLowerCase();
    const matches = (i: MenuItemDto) => !q || i.name.toLowerCase().includes(q) || (i.nameAr ?? '').includes(q);

    return this.catalog().categories.flatMap(c => {
      const subs = c.subcategories
        .map(s => ({ category: s, label: `${c.name} › ${s.name}`, sub: true, items: s.items.filter(matches) }))
        .filter(s => s.items.length)
        .map(s => ({ ...s, allIds: s.items.map(i => i.id) }));
      const items = c.items.filter(matches);
      if (!items.length && !subs.length) return [];
      const allIds = [...items.map(i => i.id), ...subs.flatMap(s => s.allIds)];
      return [{ category: c, label: c.name, sub: false, items, allIds }, ...subs];
    });
  });

  ngOnInit(): void {
    this.selected.set(new Set(this.catalog().menus.find(m => m.id === this.menu().id)?.itemIds ?? []));
  }

  protected isAll(group: Group): boolean {
    const selected = this.selected();
    return group.allIds.every(id => selected.has(id));
  }

  protected isSome(group: Group): boolean {
    const selected = this.selected();
    return group.allIds.some(id => selected.has(id));
  }

  protected countIn(group: Group): number {
    const selected = this.selected();
    return group.allIds.filter(id => selected.has(id)).length;
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
      for (const id of group.allIds) {
        if (all) next.delete(id);
        else next.add(id);
      }
      return next;
    });
  }

  protected money(minor: number): string {
    return formatMoney(minor, this.catalog().currency);
  }

  protected save(): void {
    this.error.set(null);
    this.saved.emit([...this.selected()]);
  }

  showError(message: string): void {
    this.error.set(message);
  }
}
