import { parseTip, percentOf } from './payments';

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
