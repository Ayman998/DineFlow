import { Component, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { toSignal } from '@angular/core/rxjs-interop';
import { catchError, map, of } from 'rxjs';

import { I18n } from '../../core/i18n';

/** Shown when the app is opened without a QR code. */
@Component({
  selector: 'df-home',
  templateUrl: './home.html'
})
export class Home {
  protected readonly i18n = inject(I18n);

  protected readonly apiOnline = toSignal(
    inject(HttpClient).get('/v1/public/ping').pipe(map(() => true), catchError(() => of(false)))
  );
}
