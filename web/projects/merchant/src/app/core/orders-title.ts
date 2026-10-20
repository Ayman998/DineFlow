import { Injectable, effect, inject } from '@angular/core';
import { Title } from '@angular/platform-browser';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';

import { OrderCountsStore } from './order-counts';

/**
 * Page titles with the number of new orders in front, e.g. "(2) Tables · DineFlow", so a
 * background tab still shows that orders are waiting.
 */
@Injectable({ providedIn: 'root' })
export class OrdersTitleStrategy extends TitleStrategy {
  private readonly title = inject(Title);
  private readonly counts = inject(OrderCountsStore).counts;
  private base = 'DineFlow Merchant';

  constructor() {
    super();
    effect(() => this.apply(this.counts().new));
  }

  override updateTitle(snapshot: RouterStateSnapshot): void {
    this.base = this.buildTitle(snapshot) ?? 'DineFlow Merchant';
    this.apply(this.counts().new);
  }

  private apply(newOrders: number): void {
    this.title.setTitle(newOrders ? `(${newOrders}) ${this.base}` : this.base);
  }
}
