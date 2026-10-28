import { TrendGranularity } from '../core/api';
import { Lexicon } from '../core/i18n';

const en = {
  /** The chart's accessible name, e.g. "Revenue by day". */
  by: (series: string, granularity: TrendGranularity) =>
    `${series} by ${({ Hourly: 'hour', Daily: 'day', Weekly: 'week', Monthly: 'month' } as const)[granularity]}`,
  showTable: 'Show as table',
  hideTable: 'Hide table',
  period: 'Period'
};

export const TREND_CHART: Lexicon<typeof en> = {
  en,
  ar: {
    by: (series: string, granularity: TrendGranularity) =>
      `${series} حسب ${({ Hourly: 'الساعة', Daily: 'اليوم', Weekly: 'الأسبوع', Monthly: 'الشهر' } as const)[granularity]}`,
    showTable: 'عرض كجدول',
    hideTable: 'إخفاء الجدول',
    period: 'الفترة'
  }
};
