import { Bill, equalShare, parseAmount, parseTip, percentOf, principalOf } from './payments';

describe('percentOf', () => {
  it('rounds a percentage of the bill to the nearest minor unit', () => {
    expect(percentOf(3_800, 1_000)).toBe(380);
    expect(percentOf(12_345, 1_500)).toBe(1_852);
  });
});

describe('parseTip', () => {
  it('reads an amount in the venue currency', () => {
    expect(parseTip('10', 10_000, 'AED')).toEqual({ minor: 1_000 });
    expect(parseTip('7.5', 10_000, 'AED')).toEqual({ minor: 750 });
    expect(parseTip('1.250', 10_000, 'KWD')).toEqual({ minor: 1_250 });
  });

  it('reads a percentage of the bill, including Arabic digits', () => {
    expect(parseTip('12%', 10_000, 'AED')).toEqual({ minor: 1_200 });
    expect(parseTip('١٥٪', 10_000, 'AED')).toEqual({ minor: 1_500 });
  });

  it('refuses text that is not a tip, and tips above the bill', () => {
    expect(parseTip('', 10_000, 'AED')).toBeNull();
    expect(parseTip('ten', 10_000, 'AED')).toEqual({ error: 'invalid' });
    expect(parseTip('-5', 10_000, 'AED')).toEqual({ error: 'invalid' });
    expect(parseTip('150', 10_000, 'AED')).toEqual({ error: 'tooHigh' });
  });
});

describe('equalShare', () => {
  it('rounds shares down and lets the last share absorb the rounding, like the server', () => {
    expect(equalShare(10_000, 3, 1)).toBe(3_333);
    expect(equalShare(6_667, 2, 1)).toBe(3_333);
    expect(equalShare(3_334, 1, 1)).toBe(3_334);
    expect(equalShare(12_000, 4, 2)).toBe(6_000);
  });

  it('is zero when there is nothing to split or the shares are out of range', () => {
    expect(equalShare(0, 2, 1)).toBe(0);
    expect(equalShare(1_000, 2, 3)).toBe(0);
    expect(equalShare(1_000, 0, 1)).toBe(0);
  });
});

describe('principalOf', () => {
  const bill = {
    availableMinor: 9_000,
    split: { people: 4, sharesTaken: 1, sharesLeft: 3, equalAvailable: true, itemsAvailable: true, selectedMinor: 2_500 }
  } as Bill;

  it('works out the share for each kind of split', () => {
    expect(principalOf(bill, { mode: 'Full' })).toBe(9_000);
    expect(principalOf(bill, { mode: 'Equal', shares: 1 })).toBe(3_000);
    expect(principalOf(bill, { mode: 'Items' })).toBe(2_500);
    expect(principalOf(bill, { mode: 'Custom', amountMinor: 12_000 })).toBe(9_000);
  });
});

describe('parseAmount', () => {
  it('reads amounts in the currency, including Arabic digits', () => {
    expect(parseAmount('25', 'AED')).toEqual({ minor: 2_500 });
    expect(parseAmount('25.5', 'AED')).toEqual({ minor: 2_550 });
    expect(parseAmount('١٢٫٧٥', 'AED')).toEqual({ minor: 1_275 });
    expect(parseAmount('1.250', 'KWD')).toEqual({ minor: 1_250 });
  });

  it('refuses anything that is not an amount', () => {
    expect(parseAmount('', 'AED')).toBeNull();
    expect(parseAmount('25.555', 'AED')).toEqual({ error: 'invalid' });
    expect(parseAmount('1.2.3', 'AED')).toEqual({ error: 'invalid' });
    expect(parseAmount('ten', 'AED')).toEqual({ error: 'invalid' });
  });
});
