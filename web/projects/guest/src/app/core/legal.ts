import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { catchError, of } from 'rxjs';

import { Operator } from '../pages/legal/legal.i18n';

const NONE: Operator = { legalName: null, address: null, email: null, phone: null, registrationNumber: null };

/** The company behind helloZen (the API's Legal settings), fetched once for the policy pages. */
@Injectable({ providedIn: 'root' })
export class LegalInfo {
  /** Null while loading; the empty operator if the API can't be reached, so the pages still read. */
  readonly operator = toSignal(inject(HttpClient).get<Operator>('/v1/public/legal').pipe(catchError(() => of(NONE))), { initialValue: null });
}
