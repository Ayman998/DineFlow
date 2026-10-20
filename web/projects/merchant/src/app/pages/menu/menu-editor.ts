import { Component, OnInit, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { DAYS, Day, MenuDto, MenuRequest } from '../../core/api';

/** Create or edit a menu: names, published flag and when it is served. */
@Component({
  selector: 'df-menu-editor',
  imports: [FormsModule],
  template: `
    <div class="modal-backdrop" (click)="cancelled.emit()"></div>
    <form class="modal" role="dialog" aria-modal="true" aria-labelledby="menu-editor-title" (ngSubmit)="save()">
      <h2 id="menu-editor-title" class="text-lg font-bold">{{ menu() ? 'Edit menu' : 'New menu' }}</h2>

      <div class="mt-4 grid gap-4 sm:grid-cols-2">
        <label>
          <span class="field-label">Name</span>
          <input class="field" name="name" [(ngModel)]="model.name" required maxlength="80" placeholder="e.g. Breakfast" />
        </label>
        <label>
          <span class="field-label">Arabic name <span class="font-normal text-ink-muted">(optional)</span></span>
          <input class="field" name="nameAr" dir="rtl" [(ngModel)]="model.nameAr" maxlength="80" />
        </label>
      </div>

      <fieldset class="mt-4">
        <legend class="field-label">Days</legend>
        <div class="flex flex-wrap gap-2">
          @for (day of days; track day) {
            <label class="flex items-center gap-1.5 rounded-lg border border-line px-2.5 py-1.5 text-sm">
              <input type="checkbox" class="accent-brand" [name]="'day-' + day" [ngModel]="model.days.includes(day)" (ngModelChange)="toggleDay(day)" />
              {{ day.slice(0, 3) }}
            </label>
          }
        </div>
      </fieldset>

      <fieldset class="mt-4">
        <legend class="field-label">Hours</legend>
        <label class="flex items-center gap-2 text-sm">
          <input type="checkbox" class="accent-brand" name="allDay" [(ngModel)]="allDay" /> All day
        </label>
        @if (!allDay) {
          <div class="mt-2 flex items-center gap-3">
            <input class="field max-w-36" type="time" name="start" [(ngModel)]="model.startTime" required aria-label="Start time" />
            <span>to</span>
            <input class="field max-w-36" type="time" name="end" [(ngModel)]="model.endTime" required aria-label="End time" />
          </div>
          <p class="mt-1 text-xs text-ink-muted">Venue local time. An end before the start runs past midnight.</p>
        }
      </fieldset>

      <label class="mt-4 flex items-start gap-3">
        <input type="checkbox" class="mt-1 accent-brand" name="published" [(ngModel)]="model.isPublished" />
        <span><span class="font-medium">Published</span><br /><span class="text-sm text-ink-muted">Guests only see published menus.</span></span>
      </label>

      @if (error()) { <p class="mt-4 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-800" role="alert">{{ error() }}</p> }

      <div class="mt-6 flex justify-end gap-2">
        <button type="button" class="btn-ghost" (click)="cancelled.emit()">Cancel</button>
        <button type="submit" class="btn-primary">{{ menu() ? 'Save menu' : 'Create menu' }}</button>
      </div>
    </form>
  `
})
export class MenuEditor implements OnInit {
  readonly menu = input<MenuDto | null>(null);
  readonly saved = output<MenuRequest>();
  readonly cancelled = output<void>();

  protected readonly days = DAYS;
  protected readonly error = signal<string | null>(null);
  protected allDay = true;
  protected model: MenuRequest = { name: '', nameAr: '', isPublished: false, days: [...DAYS], startTime: '', endTime: '' };

  ngOnInit(): void {
    const menu = this.menu();
    if (!menu) return;
    this.model = { name: menu.name, nameAr: menu.nameAr ?? '', isPublished: menu.isPublished, days: [...menu.days], startTime: menu.startTime ?? '', endTime: menu.endTime ?? '' };
    this.allDay = !menu.startTime;
  }

  protected toggleDay(day: Day): void {
    this.model.days = this.model.days.includes(day) ? this.model.days.filter(d => d !== day) : [...this.model.days, day];
  }

  protected save(): void {
    if (!this.model.name.trim()) return this.error.set('Enter a name.');
    if (!this.model.days.length) return this.error.set('Pick at least one day.');
    if (!this.allDay && (!this.model.startTime || !this.model.endTime)) return this.error.set('Set a start and end time, or choose All day.');
    if (!this.allDay && this.model.startTime === this.model.endTime) return this.error.set("Start and end time can't be the same.");

    this.saved.emit({
      ...this.model,
      name: this.model.name.trim(),
      nameAr: this.model.nameAr?.trim() || null,
      startTime: this.allDay ? null : this.model.startTime,
      endTime: this.allDay ? null : this.model.endTime
    });
  }

  /** Lets the page show a server-side validation message inside the dialog. */
  showError(message: string): void {
    this.error.set(message);
  }
}
