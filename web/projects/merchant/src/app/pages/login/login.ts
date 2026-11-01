import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';

import { Auth, errorMessage } from '../../core/auth';
import { I18n } from '../../core/i18n';
import { Logo } from '../../shared/logo';
import { LOGIN } from './login.i18n';

@Component({
  selector: 'hz-login',
  imports: [ReactiveFormsModule, Logo],
  templateUrl: './login.html'
})
export class Login {
  private readonly auth = inject(Auth);
  private readonly router = inject(Router);
  protected readonly i18n = inject(I18n);
  protected readonly t = this.i18n.use(LOGIN);

  /** Scan, pay, split, enjoy: the four steps on the brand board, with 24×24 stroke icons. */
  protected readonly pillars = [
    { key: 'scan', icon: 'M4 8V4h4M16 4h4v4M20 16v4h-4M8 20H4v-4M8 8h3v3H8zM13 13h3v3h-3z' },
    { key: 'pay', icon: 'M3 6h18v12H3zM3 10h18M7 15h4' },
    { key: 'split', icon: 'M9 11a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7zM2.5 20a6.5 6.5 0 0 1 13 0M16 4.5a3.5 3.5 0 0 1 0 6.5M18 14a6.5 6.5 0 0 1 3.5 6' },
    { key: 'enjoy', icon: 'M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18zM8.5 14.5a4.5 4.5 0 0 0 7 0M9 9.5h.01M15 9.5h.01' }
  ] as const;

  protected readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required]
  });

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    const { email, password } = this.form.getRawValue();

    this.auth.login(email, password).subscribe({
      next: () => this.router.navigate(['/overview']),
      error: error => {
        this.error.set(errorMessage(error));
        this.busy.set(false);
      }
    });
  }
}
