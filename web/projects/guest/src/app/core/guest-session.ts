import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, catchError, map, tap, throwError } from 'rxjs';

import { readJson, writeJson } from './storage';

export interface GuestContext {
  sessionId: string;
  expiresAt: string;
  locale: string;
  venue: { brandName: string; branchName: string; logoUrl: string | null; brandColor: string; currency: string };
  location: { type: string; label: string; displayName: string; areaName: string | null };
  features: { menu: boolean; payAtTable: boolean };
}

interface QrResolution {
  sessionToken: string;
  context: GuestContext;
}

interface StoredSession extends QrResolution {
  qrToken: string;
}

const STORAGE_KEY = 'df.session';

/**
 * The guest's table session. Scanning a QR starts one; it is kept in sessionStorage so a
 * refresh doesn't start a new session, and replaced when a different QR is scanned.
 */
@Injectable({ providedIn: 'root' })
export class GuestSession {
  private readonly http = inject(HttpClient);

  readonly current = signal<StoredSession | null>(readJson<StoredSession>('session', STORAGE_KEY));

  /** Bearer token for guest API calls, if a session is active. */
  token(): string | null {
    const session = this.current();
    return session && !isExpired(session.context) ? session.sessionToken : null;
  }

  /**
   * Opens the table for a scanned QR token. Rescanning or refreshing the same QR keeps the
   * existing session but reloads it, so venue changes (branding, features) show straight away;
   * if that session has ended on the server, a new one is started.
   */
  open(qrToken: string, locale: string): Observable<GuestContext> {
    const existing = this.current();
    if (existing?.qrToken === qrToken && !isExpired(existing.context)) {
      return this.http.get<GuestContext>(`/v1/public/sessions/${existing.context.sessionId}`).pipe(
        tap(context => this.store({ ...existing, context })),
        catchError(error => (errorCode(error) === 'network' ? throwError(() => error) : this.start(qrToken, locale)))
      );
    }

    return this.start(qrToken, locale);
  }

  private start(qrToken: string, locale: string): Observable<GuestContext> {
    return this.http
      .get<QrResolution>(`/v1/public/qr/${encodeURIComponent(qrToken)}`, { params: { locale } })
      .pipe(
        tap(resolution => this.store({ qrToken, ...resolution })),
        map(resolution => resolution.context)
      );
  }

  private store(session: StoredSession): void {
    this.current.set(session);
    writeJson('session', STORAGE_KEY, session);
  }
}

/** The ProblemDetails "code" from an API error, e.g. "qr_invalid"; "network" when there was no response. */
export function errorCode(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) return 'network';
    return (error.error as { code?: string } | null)?.code ?? `http_${error.status}`;
  }
  return 'unknown';
}

function isExpired(context: GuestContext): boolean {
  return new Date(context.expiresAt).getTime() <= Date.now();
}
