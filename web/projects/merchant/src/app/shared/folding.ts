import { signal } from '@angular/core';

/**
 * Which sections of a long list are folded away, such as the categories on the Items page.
 * Remembered in this browser per list, so staff come back to the view they left; without
 * storage (a private window, say) it simply starts unfolded.
 */
export class Folding {
  private readonly folded = signal<ReadonlySet<string>>(new Set());

  constructor(private readonly storageKey: string) {
    try {
      const saved: unknown = JSON.parse(localStorage.getItem(storageKey) ?? '[]');
      if (Array.isArray(saved)) this.folded.set(new Set(saved.filter((id): id is string => typeof id === 'string')));
    } catch {
      // Nothing remembered.
    }
  }

  isFolded(id: string): boolean {
    return this.folded().has(id);
  }

  /** Every one of these sections is folded. */
  allFolded(ids: readonly string[]): boolean {
    const folded = this.folded();
    return ids.length > 0 && ids.every(id => folded.has(id));
  }

  toggle(id: string): void {
    const next = new Set(this.folded());
    if (!next.delete(id)) next.add(id);
    this.save(next);
  }

  /** Folds all these sections, or unfolds them all if they already are. */
  toggleAll(ids: readonly string[]): void {
    const next = new Set(this.folded());
    const fold = !this.allFolded(ids);
    for (const id of ids) {
      if (fold) next.add(id);
      else next.delete(id);
    }
    this.save(next);
  }

  private save(next: ReadonlySet<string>): void {
    this.folded.set(next);
    try {
      localStorage.setItem(this.storageKey, JSON.stringify([...next]));
    } catch {
      // Not remembered, but still folded on this visit.
    }
  }
}
