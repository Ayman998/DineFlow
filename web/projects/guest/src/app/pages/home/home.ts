import { Component, DOCUMENT, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { toSignal } from '@angular/core/rxjs-interop';
import { catchError, map, of } from 'rxjs';

type Lang = 'en' | 'ar';

const TEXT: Record<Lang, { title: string; body: string; api: string; online: string; offline: string }> = {
  en: {
    title: 'Scan, order and pay at your table',
    body: 'Scan the QR code on your table to open the menu.',
    api: 'API',
    online: 'online',
    offline: 'offline'
  },
  ar: {
    title: 'امسح، اطلب وادفع من طاولتك',
    body: 'امسح رمز QR الموجود على طاولتك لفتح القائمة.',
    api: 'الخادم',
    online: 'متصل',
    offline: 'غير متصل'
  }
};

@Component({
  selector: 'df-home',
  templateUrl: './home.html'
})
export class Home {
  private readonly document = inject(DOCUMENT);

  protected readonly lang = signal<Lang>('en');
  protected readonly text = TEXT;

  protected readonly apiOnline = toSignal(
    inject(HttpClient).get('/v1/public/ping').pipe(map(() => true), catchError(() => of(false)))
  );

  protected toggleLang(): void {
    const next: Lang = this.lang() === 'en' ? 'ar' : 'en';
    this.lang.set(next);
    this.document.documentElement.lang = next;
    this.document.documentElement.dir = next === 'ar' ? 'rtl' : 'ltr';
  }
}
