import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { I18n } from './core/i18n';
import { NAV_ITEMS } from './layout/nav';
import { Shell } from './layout/shell';

describe('Shell', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Shell],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
  });

  it('should render the merchant navigation', async () => {
    const fixture = TestBed.createComponent(Shell);
    await fixture.whenStable();
    const links = (fixture.nativeElement as HTMLElement).querySelectorAll('#portal-nav nav a');
    const labels = [...links].map(a => a.textContent?.trim());
    // Go-live is for owners and managers; nobody is signed in here.
    expect(links.length).toBe(NAV_ITEMS.length - 1);
    expect(labels).toContain('Promotions');
    expect(labels).not.toContain('Go-live');
  });

  it('keeps the busiest areas in the bottom tabs and opens the drawer from "More"', async () => {
    const fixture = TestBed.createComponent(Shell);
    await fixture.whenStable();
    const host = fixture.nativeElement as HTMLElement;
    const tabBar = host.querySelector('#portal-nav ~ div > nav')!;
    expect([...tabBar.querySelectorAll('a')].map(a => a.textContent?.trim())).toEqual(['Dashboard', 'Orders', 'Tables', 'Transactions']);

    const more = tabBar.querySelector('button')!;
    expect(more.getAttribute('aria-expanded')).toBe('false');
    more.click();
    fixture.detectChanges();
    expect(more.getAttribute('aria-expanded')).toBe('true');

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    expect(more.getAttribute('aria-expanded')).toBe('false');
  });

  it('switches the navigation to Arabic and the page to right-to-left', async () => {
    const fixture = TestBed.createComponent(Shell);
    const i18n = TestBed.inject(I18n);
    i18n.lang.set('ar');
    await fixture.whenStable();
    fixture.detectChanges();

    const links = (fixture.nativeElement as HTMLElement).querySelectorAll('nav a');
    expect([...links].map(a => a.textContent?.trim())).toContain('العروض');
    expect(document.documentElement.dir).toBe('rtl');
    expect(document.documentElement.lang).toBe('ar');

    i18n.lang.set('en');
    await fixture.whenStable();
  });
});
