import { OrderPeriod } from '../../core/api';

/** A calendar day, yyyy-MM-dd. */
export type Day = string;

export type Period = Exclude<OrderPeriod, 'All'>;

/** Weeks run Saturday to Friday, as they do in Jordan and as the API counts them. */
const WEEK_START = 6;

/** The day it is (or was) at the venue at an instant. */
export function dayAt(instant: string | Date, timeZone: string): Day {
  // en-CA writes dates as yyyy-MM-dd.
  const options: Intl.DateTimeFormatOptions = { year: 'numeric', month: '2-digit', day: '2-digit' };
  try {
    return new Intl.DateTimeFormat('en-CA', { ...options, timeZone }).format(new Date(instant));
  } catch {
    return new Intl.DateTimeFormat('en-CA', options).format(new Date(instant)); // a zone this browser doesn't know
  }
}

/** The first day of the period that `day` falls in, and the first day after it. */
export function periodRange(period: Period, day: Day): { start: Day; end: Day } {
  const date = parse(day);
  const year = date.getUTCFullYear();
  const month = date.getUTCMonth();
  switch (period) {
    case 'Day':
      return { start: day, end: format(addDays(date, 1)) };
    case 'Week': {
      const start = addDays(date, -((date.getUTCDay() - WEEK_START + 7) % 7));
      return { start: format(start), end: format(addDays(start, 7)) };
    }
    case 'Month':
      return { start: format(utc(year, month)), end: format(utc(year, month + 1)) };
    case 'Quarter': {
      const first = Math.floor(month / 3) * 3;
      return { start: format(utc(year, first)), end: format(utc(year, first + 3)) };
    }
    case 'Year':
      return { start: format(utc(year, 0)), end: format(utc(year + 1, 0)) };
  }
}

/** The first day of the period before (-1) or after (1) the one `day` falls in. */
export function shiftPeriod(period: Period, day: Day, step: -1 | 1): Day {
  const { start, end } = periodRange(period, day);
  return step === 1 ? end : periodRange(period, format(addDays(parse(start), -1))).start;
}

/** "Tue, 3 Nov 2026", "31 Oct – 6 Nov 2026", "November 2026", "Q4 2026" or "2026", in `locale`. */
export function periodLabel(period: Period, day: Day, locale: string, quarter: (q: number, year: number) => string): string {
  const { start, end } = periodRange(period, day);
  const first = parse(start);
  const last = addDays(parse(end), -1);
  const date = (options: Intl.DateTimeFormatOptions) => new Intl.DateTimeFormat(locale, { ...options, timeZone: 'UTC' });
  switch (period) {
    case 'Day':
      return date({ weekday: 'short', day: 'numeric', month: 'short', year: 'numeric' }).format(first);
    case 'Week':
      return date({ day: 'numeric', month: 'short', year: 'numeric' }).formatRange(first, last);
    case 'Month':
      return date({ month: 'long', year: 'numeric' }).format(first);
    case 'Quarter':
      return quarter(Math.floor(first.getUTCMonth() / 3) + 1, first.getUTCFullYear());
    case 'Year':
      return date({ year: 'numeric' }).format(first);
  }
}

function utc(year: number, month: number): Date {
  return new Date(Date.UTC(year, month, 1));
}

function parse(day: Day): Date {
  const [year, month, date] = day.split('-').map(Number);
  return new Date(Date.UTC(year!, month! - 1, date!));
}

function format(date: Date): Day {
  return date.toISOString().slice(0, 10);
}

function addDays(date: Date, days: number): Date {
  return new Date(date.getTime() + days * 86_400_000);
}
