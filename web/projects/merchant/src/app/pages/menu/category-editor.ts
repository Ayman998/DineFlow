import { Component, OnInit, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

export interface CategoryNames {
  name: string;
  nameAr: string | null;
}

/** Create or rename a category or subcategory. */
@Component({
  selector: 'df-category-editor',
  imports: [FormsModule],
  template: `
    <div class="modal-backdrop" (click)="cancelled.emit()"></div>
    <form class="modal max-w-md" role="dialog" aria-modal="true" aria-labelledby="category-editor-title" (ngSubmit)="save()">
      <h2 id="category-editor-title" class="text-lg font-bold">{{ title() }}</h2>
      <label class="mt-4 block">
        <span class="field-label">Name</span>
        <input class="field" name="name" [(ngModel)]="name" required maxlength="80" placeholder="e.g. Mains" />
      </label>
      <label class="mt-4 block">
        <span class="field-label">Arabic name <span class="font-normal text-ink-muted">(optional)</span></span>
        <input class="field" name="nameAr" dir="rtl" [(ngModel)]="nameAr" maxlength="80" />
      </label>
      @if (error()) { <p class="mt-4 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-800" role="alert">{{ error() }}</p> }
      <div class="mt-6 flex justify-end gap-2">
        <button type="button" class="btn-ghost" (click)="cancelled.emit()">Cancel</button>
        <button type="submit" class="btn-primary">Save</button>
      </div>
    </form>
  `
})
export class CategoryEditor implements OnInit {
  readonly title = input.required<string>();
  readonly initial = input<CategoryNames | null>(null);
  readonly saved = output<CategoryNames>();
  readonly cancelled = output<void>();

  protected readonly error = signal<string | null>(null);
  protected name = '';
  protected nameAr = '';

  ngOnInit(): void {
    this.name = this.initial()?.name ?? '';
    this.nameAr = this.initial()?.nameAr ?? '';
  }

  protected save(): void {
    if (!this.name.trim()) return this.error.set('Enter a name.');
    this.saved.emit({ name: this.name.trim(), nameAr: this.nameAr.trim() || null });
  }

  showError(message: string): void {
    this.error.set(message);
  }
}
