import { Component, ElementRef, afterNextRender, inject, input, output, signal, viewChild } from '@angular/core';

import { I18n } from '../core/i18n';

/**
 * Asks before a delete. The page runs the delete on `confirmed`, closes the dialog when it's
 * done, and shows a failure here with `showError` so staff can try again or cancel.
 */
@Component({
  selector: 'hz-confirm-dialog',
  template: `
    @let common = i18n.t();
    <div class="modal-backdrop" (click)="cancel()"></div>
    <div class="modal max-w-md" role="alertdialog" aria-modal="true" aria-labelledby="confirm-title" aria-describedby="confirm-message"
         (keydown.escape)="cancel()">
      <h2 id="confirm-title" class="text-lg font-bold">{{ title() }}</h2>
      <p id="confirm-message" class="mt-2 text-sm text-ink-muted">{{ message() }}</p>

      @if (error()) { <p class="mt-4 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-800" role="alert">{{ error() }}</p> }

      <div class="mt-6 flex justify-end gap-2">
        <button #cancelButton type="button" class="btn-ghost" [disabled]="busy()" (click)="cancel()">{{ common.cancel }}</button>
        <button type="button" class="btn-primary bg-red-700 text-white hover:bg-red-800" [disabled]="busy()" (click)="confirm()">
          {{ busy() ? common.deleting : common.delete }}
        </button>
      </div>
    </div>
  `
})
export class ConfirmDialog {
  protected readonly i18n = inject(I18n);

  readonly title = input.required<string>();
  readonly message = input.required<string>();
  readonly confirmed = output<void>();
  readonly cancelled = output<void>();

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  private readonly cancelButton = viewChild.required<ElementRef<HTMLButtonElement>>('cancelButton');

  constructor() {
    // Focus the safe choice, so Escape and Enter don't delete by accident.
    afterNextRender(() => this.cancelButton().nativeElement.focus());
  }

  protected confirm(): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    this.confirmed.emit();
  }

  protected cancel(): void {
    if (!this.busy()) this.cancelled.emit();
  }

  /** Lets the page show why the delete failed. */
  showError(message: string): void {
    this.busy.set(false);
    this.error.set(message);
  }
}
