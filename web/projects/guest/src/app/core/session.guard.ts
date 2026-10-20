import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { GuestSession } from './guest-session';

/** Menu and order pages need a table session; without one the guest is asked to scan a QR. */
export const sessionGuard: CanActivateFn = () =>
  inject(GuestSession).token() ? true : inject(Router).createUrlTree(['/']);
