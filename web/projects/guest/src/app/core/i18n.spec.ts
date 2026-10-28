import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { I18n, languageInterceptor } from './i18n';

describe('languageInterceptor', () => {
  beforeEach(() => {
    localStorage.removeItem('df.lang');
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([languageInterceptor])), provideHttpClientTesting()]
    });
  });

  it("sends the guest's language on API calls, so error messages come back in it", () => {
    const http = TestBed.inject(HttpClient);
    const backend = TestBed.inject(HttpTestingController);
    const i18n = TestBed.inject(I18n);

    http.get('/v1/public/ping').subscribe();
    expect(backend.expectOne('/v1/public/ping').request.headers.get('Accept-Language')).toBe('en');

    i18n.lang.set('ar');
    http.get('/v1/public/ping').subscribe();
    expect(backend.expectOne('/v1/public/ping').request.headers.get('Accept-Language')).toBe('ar');

    http.get('/assets/x.json').subscribe();
    expect(backend.expectOne('/assets/x.json').request.headers.has('Accept-Language')).toBe(false);
    backend.verify();
  });
});

describe('server texts in Arabic', () => {
  it('translates provider failure reasons and leaves unknown ones out', () => {
    const i18n = TestBed.inject(I18n);
    i18n.lang.set('ar');
    const t = i18n.t();

    expect(t.failureReason('Insufficient funds.')).toBe('الرصيد غير كافٍ.');
    expect(t.failureReason("The payment didn't go through (BLOCKED).")).toBe('لم تتم عملية الدفع (BLOCKED).');
    expect(t.failureReason('Something new')).toBeNull();
    expect(t.feedbackTitle('Sorry we missed the mark')).not.toContain('Sorry');
    expect(t.feedbackTitle('Our own heading')).toBe('Our own heading');
  });
});
