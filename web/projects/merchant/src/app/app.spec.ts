import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

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
    const links = (fixture.nativeElement as HTMLElement).querySelectorAll('nav a');
    expect(links.length).toBe(NAV_ITEMS.length);
    expect([...links].map(a => a.textContent?.trim())).toContain('Promotions');
  });
});
