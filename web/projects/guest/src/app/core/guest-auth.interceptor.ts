import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';

import { GuestSession } from './guest-session';

/** Sends the guest session token on session-scoped API calls. */
export const guestAuthInterceptor: HttpInterceptorFn = (req, next) => {
  const token = inject(GuestSession).token();
  if (!token || !req.url.startsWith('/v1/public/sessions')) return next(req);

  return next(req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }));
};
