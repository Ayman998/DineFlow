import { Injectable, effect, inject, signal } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { ActivatedRouteSnapshot, RouterStateSnapshot, TitleStrategy } from '@angular/router';

import { I18n } from './i18n';
import { OrderCountsStore } from './order-counts';

/**
 * Page titles in the portal's language with the number of new orders in front, e.g.
 * "(2) Tables · DineFlow", so a background tab still shows that orders are waiting.
 */
@Injectable({ providedIn: 'root' })
export class OrdersTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);
  private readonly counts = inject(OrderCountsStore).counts;
  private readonly t = inject(I18n).t;
  /** The route's "titleKey": a navigation path, or "signIn". */
  private readonly key = signal<string | null>(null);

  constructor() {
    super();
    effect(() => {
      const key = this.key();
      const t = this.t();
      const page = key === 'signIn' ? t.signIn : key ? t.nav[key] : null;
      const base = page ? `${page} · DineFlow` : 'DineFlow Merchant';
      const newOrders = this.counts().new;
      this.title.setTitle(newOrders ? `(${newOrders}) ${base}` : base);
    });
  }

  override updateTitle(snapshot: RouterStateSnapshot): void {
    let route: ActivatedRouteSnapshot | null = snapshot.root;
    let key: string | null = null;
    while (route) {
      key = (route.data['titleKey'] as string | undefined) ?? key;
      route = route.firstChild;
    }
    this.key.set(key);
  }
}
