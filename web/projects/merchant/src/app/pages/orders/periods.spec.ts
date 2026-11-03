import { dayAt, periodLabel, periodRange, shiftPeriod } from './periods';

describe('order periods', () => {
  it('counts weeks from Saturday to Friday', () => {
    // 3 Nov 2026 is a Tuesday.
    expect(periodRange('Week', '2026-11-03')).toEqual({ start: '2026-10-31', end: '2026-11-07' });
    expect(periodRange('Week', '2026-10-31')).toEqual({ start: '2026-10-31', end: '2026-11-07' });
    expect(periodRange('Week', '2026-11-06')).toEqual({ start: '2026-10-31', end: '2026-11-07' });
  });

  it('finds the day, month, quarter and year around a date', () => {
    expect(periodRange('Day', '2026-12-31')).toEqual({ start: '2026-12-31', end: '2027-01-01' });
    expect(periodRange('Month', '2026-02-14')).toEqual({ start: '2026-02-01', end: '2026-03-01' });
    expect(periodRange('Quarter', '2026-11-03')).toEqual({ start: '2026-10-01', end: '2027-01-01' });
    expect(periodRange('Year', '2026-11-03')).toEqual({ start: '2026-01-01', end: '2027-01-01' });
  });

  it('steps to the period before or after', () => {
    expect(shiftPeriod('Month', '2026-03-31', -1)).toBe('2026-02-01');
    expect(shiftPeriod('Week', '2026-11-03', 1)).toBe('2026-11-07');
    expect(shiftPeriod('Quarter', '2026-01-15', -1)).toBe('2025-10-01');
  });

  it('reads the day at the venue, not in the browser', () => {
    expect(dayAt('2026-11-03T22:30:00Z', 'Asia/Amman')).toBe('2026-11-04'); // 01:30 in Amman
    expect(dayAt('2026-11-03T20:30:00Z', 'Asia/Amman')).toBe('2026-11-03');
  });

  it('labels quarters and years', () => {
    const quarter = (q: number, year: number) => `Q${q} ${year}`;
    expect(periodLabel('Quarter', '2026-11-03', 'en-GB', quarter)).toBe('Q4 2026');
    expect(periodLabel('Year', '2026-11-03', 'en-GB', quarter)).toBe('2026');
  });
});
