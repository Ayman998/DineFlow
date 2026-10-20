/**
 * Browser storage can be missing or throw (private windows, blocked site data), so every
 * read and write goes through these wrappers and the app keeps working without it.
 */
export function readJson<T>(storage: 'local' | 'session', key: string): T | null {
  try {
    const raw = (storage === 'local' ? localStorage : sessionStorage).getItem(key);
    return raw ? (JSON.parse(raw) as T) : null;
  } catch {
    return null;
  }
}

export function writeJson(storage: 'local' | 'session', key: string, value: unknown): void {
  try {
    const store = storage === 'local' ? localStorage : sessionStorage;
    if (value === null) store.removeItem(key);
    else store.setItem(key, JSON.stringify(value));
  } catch {
    // Storage unavailable: the value just isn't remembered.
  }
}
