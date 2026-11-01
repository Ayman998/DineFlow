import { Component, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { toSignal } from '@angular/core/rxjs-interop';
import { catchError, map, of } from 'rxjs';

import { I18n } from '../../core/i18n';
import { Logo } from '../../shared/logo';

/** Shown when the app is opened without a QR code. */
@Component({
  selector: 'hz-home',
  imports: [Logo],
  templateUrl: './home.html'
})
export class Home {
  protected readonly i18n = inject(I18n);

  protected readonly apiOnline = toSignal(
    inject(HttpClient).get('/v1/public/ping').pipe(map(() => true), catchError(() => of(false)))
  );

  /** Scan, pay, split, enjoy: the four steps on the brand board, with 24×24 stroke icons. */
  protected readonly pillars = [
    { key: 'scan', icon: 'M4 8V4h4M16 4h4v4M20 16v4h-4M8 20H4v-4M8 8h3v3H8zM13 13h3v3h-3z' },
    { key: 'pay', icon: 'M3 6h18v12H3zM3 10h18M7 15h4' },
    { key: 'split', icon: 'M9 11a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7zM2.5 20a6.5 6.5 0 0 1 13 0M16 4.5a3.5 3.5 0 0 1 0 6.5M18 14a6.5 6.5 0 0 1 3.5 6' },
    { key: 'enjoy', icon: 'M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18zM8.5 14.5a4.5 4.5 0 0 0 7 0M9 9.5h.01M15 9.5h.01' }
  ] as const;
}
