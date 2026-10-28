import { OverviewPeriod, TrendGranularity } from '../../core/api';
import { Lexicon } from '../../core/i18n';

/**
 * An Arabic count: the singular and dual stand alone ("طلب واحد", "طلبان"); 3–10 take the
 * plural ("3 طلبات"); 0, 11 and up take the accusative singular ("15 طلباً").
 */
function arCount(n: number, one: string, two: string, few: string, many: string): string {
  if (n === 1) return one;
  if (n === 2) return two;
  const rest = n % 100;
  return rest >= 3 && rest <= 10 ? `${n} ${few}` : `${n} ${many}`;
}

/** A "Needs attention" item's values for its wording, amounts and places already formatted. */
export interface AttentionValues {
  count: number;
  /** payment_failures: where the last one was, e.g. "Table 4", unless it was a payment link. */
  place: string;
  linkCode: string | null;
  time: string;
  /** pos_stuck: how many of them the POS refused. */
  refused: number;
  /** tables_*: the open balance. */
  open: string;
  minutes: number;
}

type Attention = (v: AttentionValues) => { title: string; detail: string };

const en = {
  title: 'Overview',
  updatedLive: 'Updated live as orders and payments come in',
  period: 'Period',
  periods: { Today: 'Today', Week: 'Last 7 days', Month: 'This month', Quarter: 'This quarter' } as Record<OverviewPeriod, string>,
  /** Follows a card title: "Revenue this month". */
  periodNames: { Today: 'today', Week: 'last 7 days', Month: 'this month', Quarter: 'this quarter' } as Record<OverviewPeriod, string>,
  revenue: 'Revenue',
  tips: 'Tips',
  orders: 'Orders',
  refunds: 'Refunds',
  revenueToday: 'Revenue today',
  tipsToday: 'Tips today',
  ordersToday: 'Orders today',
  paymentFailuresToday: 'Payment failures today',
  successfulPaymentsToday: 'Successful payments today',
  /** "▲ 5.57% vs last week". */
  vsLastWeek: (change: string) => `${change} vs last week`,
  whatToChart: 'What to chart',
  by: 'By',
  granularities: { Hourly: 'Hour', Daily: 'Day', Weekly: 'Week', Monthly: 'Month' } as Record<TrendGranularity, string>,
  needsAttention: 'Needs attention',
  nothingNow: 'Nothing right now. Failed payments, open balances, waiting orders and unhappy guests show up here.',
  attentionKinds: {
    payment: 'Payment', tables: 'Tables', review: 'Review', refund: 'Refund', pos: 'POS', reconciliation: 'Reconciliation', orders: 'Orders'
  } as Partial<Record<string, string>>,
  /** Each item, by the API's code. */
  attention: {
    payment_failures: v => ({
      title: v.count === 1 ? '1 payment failure today' : `${v.count} payment failures today`,
      detail: `Last: ${v.linkCode ? `Payment link ${v.linkCode}` : v.place} · ${v.time}`
    }),
    pos_stuck: v => ({
      title: v.count === 1 ? '1 item not in the POS yet' : `${v.count} items not in the POS yet`,
      detail: v.refused > 0
        ? `${v.refused} refused by the POS: fix and retry under Settings → POS`
        : 'Waiting for the POS to come back; nobody is charged again'
    }),
    reconciliation: v => ({
      title: v.count === 1 ? '1 reconciliation difference' : `${v.count} reconciliation differences`,
      detail: "Between DineFlow, the acquirer's settlements and the POS"
    }),
    orders_waiting: v => ({
      title: v.count === 1 ? '1 order waiting to be accepted' : `${v.count} orders waiting to be accepted`,
      detail: `Placed more than ${v.minutes} minutes ago`
    }),
    tables_partially_paid: v => ({
      title: v.count === 1 ? '1 table partially paid' : `${v.count} tables partially paid`,
      detail: `Open balance ${v.open}`
    }),
    tables_unpaid: v => ({
      title: v.count === 1 ? '1 table with an unpaid bill' : `${v.count} tables with unpaid bills`,
      detail: `Open balance ${v.open}`
    }),
    unhappy_feedback: v => ({
      title: v.count === 1 ? '1 new 1–3 star feedback' : `${v.count} new 1–3 star feedbacks`,
      detail: 'Follow up with guests'
    }),
    refunds_pending: v => ({
      title: v.count === 1 ? '1 refund waiting for the provider' : `${v.count} refunds waiting for the provider`,
      detail: 'They complete when the provider confirms'
    })
  } as Record<string, Attention>
};

export const OVERVIEW: Lexicon<typeof en> = {
  en,
  ar: {
    title: 'نظرة عامة',
    updatedLive: 'تُحدَّث مباشرةً مع وصول الطلبات والدفعات',
    period: 'الفترة',
    periods: { Today: 'اليوم', Week: 'آخر 7 أيام', Month: 'هذا الشهر', Quarter: 'هذا الربع' },
    periodNames: { Today: 'اليوم', Week: 'خلال آخر 7 أيام', Month: 'هذا الشهر', Quarter: 'هذا الربع' },
    revenue: 'الإيرادات',
    tips: 'الإكراميات',
    orders: 'الطلبات',
    refunds: 'المبالغ المستردة',
    revenueToday: 'إيرادات اليوم',
    tipsToday: 'إكراميات اليوم',
    ordersToday: 'طلبات اليوم',
    paymentFailuresToday: 'الدفعات الفاشلة اليوم',
    successfulPaymentsToday: 'الدفعات الناجحة اليوم',
    vsLastWeek: (change: string) => `${change} مقارنةً بالأسبوع الماضي`,
    whatToChart: 'البيانات المعروضة في الرسم البياني',
    by: 'حسب',
    granularities: { Hourly: 'الساعة', Daily: 'اليوم', Weekly: 'الأسبوع', Monthly: 'الشهر' },
    needsAttention: 'يحتاج إلى متابعة',
    nothingNow: 'لا شيء حالياً. تظهر هنا الدفعات الفاشلة والفواتير غير المسددة والطلبات المنتظرة والضيوف غير الراضين.',
    attentionKinds: {
      payment: 'دفعة', tables: 'الطاولات', review: 'تقييم', refund: 'استرداد', pos: 'نقاط البيع', reconciliation: 'مطابقة', orders: 'الطلبات'
    },
    attention: {
      payment_failures: v => ({
        title: arCount(v.count, 'دفعة فاشلة واحدة اليوم', 'دفعتان فاشلتان اليوم', 'دفعات فاشلة اليوم', 'دفعة فاشلة اليوم'),
        detail: `آخرها: ${v.linkCode ? `رابط الدفع ${v.linkCode}` : v.place} · ${v.time}`
      }),
      pos_stuck: v => ({
        title: arCount(v.count, 'عملية واحدة لم تصل إلى نظام نقاط البيع بعد', 'عمليتان لم تصلا إلى نظام نقاط البيع بعد',
          'عمليات لم تصل إلى نظام نقاط البيع بعد', 'عملية لم تصل إلى نظام نقاط البيع بعد'),
        detail: v.refused > 0
          ? `رفض نظام نقاط البيع ${v.refused} منها: صحّحها وأعد المحاولة من الإعدادات ← نقاط البيع`
          : 'بانتظار عودة نظام نقاط البيع؛ لن يُخصم أي مبلغ مرة أخرى'
      }),
      reconciliation: v => ({
        title: arCount(v.count, 'فرق واحد في المطابقة', 'فرقان في المطابقة', 'فروقات في المطابقة', 'فرقاً في المطابقة'),
        detail: 'بين DineFlow وتسويات البنك المستحوذ ونظام نقاط البيع'
      }),
      orders_waiting: v => ({
        title: arCount(v.count, 'طلب واحد بانتظار القبول', 'طلبان بانتظار القبول', 'طلبات بانتظار القبول', 'طلباً بانتظار القبول'),
        detail: `قُدّمت منذ أكثر من ${arCount(v.minutes, 'دقيقة', 'دقيقتين', 'دقائق', 'دقيقة')}`
      }),
      tables_partially_paid: v => ({
        title: arCount(v.count, 'طاولة واحدة مدفوعة جزئياً', 'طاولتان مدفوعتان جزئياً', 'طاولات مدفوعة جزئياً', 'طاولة مدفوعة جزئياً'),
        detail: `الرصيد المتبقي ${v.open}`
      }),
      tables_unpaid: v => ({
        title: arCount(v.count, 'طاولة واحدة بفاتورة غير مدفوعة', 'طاولتان بفواتير غير مدفوعة', 'طاولات بفواتير غير مدفوعة', 'طاولة بفواتير غير مدفوعة'),
        detail: `الرصيد المتبقي ${v.open}`
      }),
      unhappy_feedback: v => ({
        title: arCount(v.count, 'تقييم جديد واحد من 1–3 نجوم', 'تقييمان جديدان من 1–3 نجوم', 'تقييمات جديدة من 1–3 نجوم', 'تقييماً جديداً من 1–3 نجوم'),
        detail: 'تابِع مع الضيوف'
      }),
      refunds_pending: v => ({
        title: arCount(v.count, 'استرداد واحد بانتظار مزوّد الدفع', 'استردادان بانتظار مزوّد الدفع', 'استردادات بانتظار مزوّد الدفع', 'استرداداً بانتظار مزوّد الدفع'),
        detail: 'تكتمل عند تأكيد مزوّد الدفع'
      })
    }
  }
};
