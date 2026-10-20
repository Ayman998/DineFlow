import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';

import { GuestSession } from './guest-session';

/** Guest API calls that act for the session: its menu, orders and bill, and its payments. */
const SESSION_SCOPED = ['/v1/public/sessions', '/v1/public/payments'];

/** Sends the guest session token on session-scoped API calls. */
export const guestAuthInterceptor: HttpInterceptorFn = (req, next) => {
  const token = inject(GuestSession).token();
  if (!token || !SESSION_SCOPED.some(prefix => req.url.startsWith(prefix))) return next(req);

  return next(req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }));
};
