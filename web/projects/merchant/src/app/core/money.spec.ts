import { formatMoney, parseMajor, toMajorString } from './money';

describe('money', () => {
  it('parses prices into minor units for the currency', () => {
    expect(parseMajor('29.50', 'AED')).toBe(2950);
    expect(parseMajor('29.5', 'AED')).toBe(2950);
    expect(parseMajor(' 29 ', 'AED')).toBe(2900);
    expect(parseMajor('0', 'AED')).toBe(0);
    expect(parseMajor('1.234', 'KWD')).toBe(1234);
  });

  it('rejects text that is not a valid amount', () => {
    for (const text of ['', 'abc', '-1', '1,000', '1.234', '1.2.3']) {
      expect(parseMajor(text, 'AED')).toBeNull();
    }
  });

  it('formats minor units back for display and editing', () => {
    expect(toMajorString(2950, 'AED')).toBe('29.50');
    expect(toMajorString(1234, 'KWD')).toBe('1.234');
    expect(formatMoney(2950, 'AED')).toContain('29.50');
  });
});
