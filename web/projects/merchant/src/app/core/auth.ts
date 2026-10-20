import { HttpClient, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Observable, catchError, tap, throwError } from 'rxjs';

export interface StaffProfile {
  id: string;
  email: string;
  displayName: string;
  role: 'Owner' | 'Manager' | 'Staff';
  tenantId: string;
  tenantName: string;
}

interface LoginResult {
  token: string;
  expiresAt: string;
  profile: StaffProfile;
}

const STORAGE_KEY = 'df.merchant.auth';

/** Merchant sign-in state. The JWT lives in localStorage until it expires or the user signs out. */
@Injectable({ providedIn: 'root' })
export class Auth {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  private readonly session = signal<LoginResult | null>(readSession());

  readonly profile = computed(() => this.session()?.profile ?? null);
  readonly canManage = computed(() => ['Owner', 'Manager'].includes(this.profile()?.role ?? ''));

  token(): string | null {
    const session = this.session();
    if (!session) return null;
    if (new Date(session.expiresAt).getTime() <= Date.now()) {
      this.clear();
      return null;
    }
    return session.token;
  }

  login(email: string, password: string): Observable<LoginResult> {
    return this.http.post<LoginResult>('/v1/merchant/auth/login', { email, password }).pipe(
      tap(result => {
        this.session.set(result);
        writeSession(result);
      })
    );
  }

  logout(): void {
    this.clear();
    this.router.navigate(['/login']);
  }

  private clear(): void {
    this.session.set(null);
    writeSession(null);
  }
}

/** Adds the bearer token to merchant API calls and sends the user to sign-in on a 401. */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith('/v1/merchant') || req.url.endsWith('/auth/login')) return next(req);

  const auth = inject(Auth);
  const token = auth.token();
  const request = token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;

  return next(request).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401) auth.logout();
      return throwError(() => error);
    })
  );
};

export const authGuard: CanActivateFn = () => {
  const auth = inject(Auth);
  return auth.token() ? true : inject(Router).createUrlTree(['/login']);
};

/** The API's validation or ProblemDetails message, or a generic one when there was no usable response. */
export function errorMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) return "Can't reach the DineFlow API. Is it running?";
    if (error.status === 403) return "Your role can't make this change. Ask an owner or manager.";
    const body = error.error as { title?: string; errors?: Record<string, string[]> } | null;
    const firstFieldError = body?.errors ? Object.values(body.errors)[0]?.[0] : undefined;
    return firstFieldError ?? body?.title ?? `Request failed (${error.status}).`;
  }
  return 'Something went wrong.';
}

function readSession(): LoginResult | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    return raw ? (JSON.parse(raw) as LoginResult) : null;
  } catch {
    return null;
  }
}

function writeSession(value: LoginResult | null): void {
  try {
    if (value) localStorage.setItem(STORAGE_KEY, JSON.stringify(value));
    else localStorage.removeItem(STORAGE_KEY);
  } catch {
    // Storage unavailable: the user signs in again next time.
  }
}
