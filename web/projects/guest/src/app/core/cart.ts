import { Injectable, computed, effect, inject, signal } from '@angular/core';

import { GuestSession } from './guest-session';
import { readJson, writeJson } from './storage';

export interface CartOption {
  groupId: string;
  optionId: string;
  name: string;
  nameAr: string | null;
  priceDeltaMinor: number;
}

export interface CartLine {
  /** Same item + options + note → same key, so adding it again just raises the quantity. */
  key: string;
  itemId: string;
  name: string;
  nameAr: string | null;
  imageUrl?: string | null;
  /** Item price plus selected options, per unit. Shown to the guest; the server re-prices at checkout. */
  unitPriceMinor: number;
  quantity: number;
  options: CartOption[];
  note: string;
}

export const MAX_QUANTITY = 99;

/**
 * The guest's order in progress, kept per table session in sessionStorage. Prices here are for
 * display; the server recalculates the authoritative total when the order is placed (Sprint 3).
 */
@Injectable({ providedIn: 'root' })
export class Cart {
  private readonly session = inject(GuestSession);
  private readonly sessionId = computed(() => this.session.current()?.context.sessionId ?? null);

  readonly lines = signal<CartLine[]>([]);
  readonly count = computed(() => this.lines().reduce((sum, l) => sum + l.quantity, 0));
  readonly totalMinor = computed(() => this.lines().reduce((sum, l) => sum + l.unitPriceMinor * l.quantity, 0));

  constructor() {
    // Load the cart that belongs to the current session, and save every change to it.
    effect(() => {
      const id = this.sessionId();
      this.lines.set(id ? (readJson<CartLine[]>('session', storageKey(id)) ?? []) : []);
    });
    effect(() => {
      const id = this.sessionId();
      if (id) writeJson('session', storageKey(id), this.lines());
    });
  }

  add(line: Omit<CartLine, 'key'>): void {
    const key = lineKey(line.itemId, line.options, line.note);
    this.lines.update(lines => {
      const existing = lines.find(l => l.key === key);
      return existing
        ? lines.map(l => (l.key === key ? { ...l, quantity: Math.min(MAX_QUANTITY, l.quantity + line.quantity) } : l))
        : [...lines, { ...line, key }];
    });
  }

  setQuantity(key: string, quantity: number): void {
    this.lines.update(lines =>
      quantity <= 0
        ? lines.filter(l => l.key !== key)
        : lines.map(l => (l.key === key ? { ...l, quantity: Math.min(MAX_QUANTITY, quantity) } : l))
    );
  }

  /** Empties the order once it has been placed. */
  clear(): void {
    this.lines.set([]);
  }

  /** Total quantity of an item across all its option combinations. */
  quantityOf(itemId: string): number {
    return this.lines().filter(l => l.itemId === itemId).reduce((sum, l) => sum + l.quantity, 0);
  }

  /** The cart line for an item ordered as-is (no options, no note), if any. */
  plainLine(itemId: string): CartLine | undefined {
    return this.lines().find(l => l.key === lineKey(itemId, [], ''));
  }
}

function lineKey(itemId: string, options: CartOption[], note: string): string {
  return [itemId, ...options.map(o => o.optionId).sort(), note.trim()].join('|');
}

function storageKey(sessionId: string): string {
  return `df.cart.${sessionId}`;
}
