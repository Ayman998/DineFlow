/** Decimal places of a currency: 2 for AED, 3 for KWD/BHD/OMR/JOD. */
export function minorDigits(currency: string): number {
  return new Intl.NumberFormat('en', { style: 'currency', currency }).resolvedOptions().maximumFractionDigits ?? 2;
}

/** 2750 AED → "AED 27.50". */
export function formatMoney(minor: number, currency: string): string {
  const digits = minorDigits(currency);
  return new Intl.NumberFormat('en', { style: 'currency', currency, minimumFractionDigits: digits, maximumFractionDigits: digits })
    .format(minor / 10 ** digits);
}

/** 2750 AED → "27.50", for a price input. */
export function toMajorString(minor: number, currency: string): string {
  const digits = minorDigits(currency);
  return (minor / 10 ** digits).toFixed(digits);
}

/** "27.5" AED → 2750; null when the text isn't a valid non-negative amount for the currency. */
export function parseMajor(text: string, currency: string): number | null {
  const digits = minorDigits(currency);
  const trimmed = text.trim();
  const match = /^(\d+)(?:\.(\d*))?$/.exec(trimmed);
  if (!match) return null;
  const [, whole, fraction = ''] = match;
  if (fraction.length > digits) return null;
  return Number(whole) * 10 ** digits + Number(fraction.padEnd(digits, '0') || 0);
}
