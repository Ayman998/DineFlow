import { Component, OnInit, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { ALLERGENS, Allergen, ItemRequest, MenuAdminApi, MenuItemDto } from '../../core/api';
import { errorMessage } from '../../core/auth';
import { I18n } from '../../core/i18n';
import { parseMajor, toMajorString } from '../../core/money';
import { MENU } from './menu.i18n';

interface OptionModel {
  id: string | null;
  name: string;
  nameAr: string;
  price: string;
  isDefault: boolean;
  isAvailable: boolean;
}

interface GroupModel {
  id: string | null;
  name: string;
  nameAr: string;
  minSelect: number;
  maxSelect: number;
  options: OptionModel[];
}

export interface CategoryChoice {
  id: string;
  label: string;
}

/**
 * Create or edit a menu item (blueprint 4.4 "Menu"): names in English and Arabic, price,
 * photo, allergens, tags, availability and modifier groups with their options.
 */
@Component({
  selector: 'df-item-editor',
  imports: [FormsModule],
  templateUrl: './item-editor.html'
})
export class ItemEditor implements OnInit {
  private readonly api = inject(MenuAdminApi);
  protected readonly i18n = inject(I18n);
  protected readonly t = this.i18n.use(MENU);

  readonly item = input<MenuItemDto | null>(null);
  readonly categoryId = input.required<string>();
  readonly categories = input.required<CategoryChoice[]>();
  readonly currency = input.required<string>();

  readonly saved = output<ItemRequest>();
  readonly cancelled = output<void>();

  protected readonly allergens = ALLERGENS;
  protected readonly error = signal<string | null>(null);
  protected readonly uploading = signal(false);

  protected model = {
    categoryId: '',
    name: '',
    nameAr: '',
    description: '',
    descriptionAr: '',
    price: '',
    imageUrl: null as string | null,
    isAvailable: true,
    allergens: new Set<Allergen>(),
    tags: '',
    groups: [] as GroupModel[]
  };

  ngOnInit(): void {
    const item = this.item();
    const currency = this.currency();
    this.model.categoryId = item?.categoryId ?? this.categoryId();
    if (!item) return;

    this.model = {
      categoryId: item.categoryId,
      name: item.name,
      nameAr: item.nameAr ?? '',
      description: item.description ?? '',
      descriptionAr: item.descriptionAr ?? '',
      price: toMajorString(item.priceMinor, currency),
      imageUrl: item.imageUrl,
      isAvailable: item.isAvailable,
      allergens: new Set(item.allergens),
      tags: item.tags.join(', '),
      groups: item.modifierGroups.map(g => ({
        id: g.id, name: g.name, nameAr: g.nameAr ?? '', minSelect: g.minSelect, maxSelect: g.maxSelect,
        options: g.options.map(o => ({
          id: o.id, name: o.name, nameAr: o.nameAr ?? '', price: toMajorString(o.priceDeltaMinor, currency),
          isDefault: o.isDefault, isAvailable: o.isAvailable
        }))
      }))
    };
  }

  protected toggleAllergen(allergen: Allergen): void {
    if (this.model.allergens.has(allergen)) this.model.allergens.delete(allergen);
    else this.model.allergens.add(allergen);
  }

  protected upload(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    if (file.size > 5 * 1024 * 1024) return this.error.set(this.t().item.imageTooLarge);

    this.uploading.set(true);
    this.error.set(null);
    this.api.uploadImage(file).subscribe({
      next: result => {
        this.model.imageUrl = result.url;
        this.uploading.set(false);
      },
      error: error => {
        this.error.set(errorMessage(error));
        this.uploading.set(false);
      }
    });
  }

  protected addGroup(): void {
    this.model.groups.push({ id: null, name: '', nameAr: '', minSelect: 0, maxSelect: 1, options: [this.newOption()] });
  }

  protected removeGroup(index: number): void {
    this.model.groups.splice(index, 1);
  }

  protected moveGroup(index: number, step: -1 | 1): void {
    const groups = this.model.groups;
    const target = index + step;
    if (target < 0 || target >= groups.length) return;
    [groups[index], groups[target]] = [groups[target]!, groups[index]!];
  }

  protected addOption(group: GroupModel): void {
    group.options.push(this.newOption());
  }

  protected removeOption(group: GroupModel, index: number): void {
    group.options.splice(index, 1);
  }

  protected save(): void {
    const currency = this.currency();
    const m = this.model;
    const fail = (message: string) => this.error.set(message);
    const t = this.t().item;

    if (!m.name.trim()) return fail(t.enterItemName);
    const priceMinor = parseMajor(m.price, currency);
    if (priceMinor === null) return fail(t.invalidPrice(currency));

    const tags = m.tags.split(/[,،]/).map(t => t.trim()).filter(Boolean);
    if (tags.length > 8) return fail(t.tooManyTags);
    if (tags.some(t => t.length > 24)) return fail(t.tagTooLong);

    const groups: ItemRequest['modifierGroups'] = [];
    for (const g of m.groups) {
      const label = g.name.trim() || t.aGroup;
      if (!g.name.trim()) return fail(t.groupNeedsName);
      if (!g.options.length) return fail(t.groupNeedsOption(label));
      if (g.minSelect < 0 || g.maxSelect < 1 || g.minSelect > g.maxSelect)
        return fail(t.pickRange(label));
      if (g.maxSelect > g.options.length) return fail(t.maxOverOptions(label));
      if (g.options.filter(o => o.isDefault).length > g.maxSelect)
        return fail(t.tooManyDefaults(label));

      const options: ItemRequest['modifierGroups'][number]['options'] = [];
      for (const o of g.options) {
        if (!o.name.trim()) return fail(t.optionNeedsName(label));
        const delta = parseMajor(o.price || '0', currency);
        if (delta === null) return fail(t.optionInvalidPrice(o.name));
        options.push({ id: o.id, name: o.name.trim(), nameAr: o.nameAr.trim() || null, priceDeltaMinor: delta, isDefault: o.isDefault, isAvailable: o.isAvailable });
      }
      groups.push({ id: g.id, name: g.name.trim(), nameAr: g.nameAr.trim() || null, minSelect: g.minSelect, maxSelect: g.maxSelect, options });
    }

    this.error.set(null);
    this.saved.emit({
      categoryId: m.categoryId,
      name: m.name.trim(),
      nameAr: m.nameAr.trim() || null,
      description: m.description.trim() || null,
      descriptionAr: m.descriptionAr.trim() || null,
      priceMinor,
      imageUrl: m.imageUrl,
      isAvailable: m.isAvailable,
      allergens: ALLERGENS.map(a => a.value).filter(a => m.allergens.has(a)),
      tags,
      modifierGroups: groups
    });
  }

  showError(message: string): void {
    this.error.set(message);
  }

  private newOption(): OptionModel {
    return { id: null, name: '', nameAr: '', price: '0', isDefault: false, isAvailable: true };
  }
}
