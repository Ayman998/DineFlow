import { OrderFilter } from '../../core/api';
import { Lexicon } from '../../core/i18n';

const en = {
  title: 'Orders',
  intro: 'New orders appear here instantly, with a chime. Click anywhere once to allow the sound.',
  filters: { Active: 'Active', New: 'New', Completed: 'Completed', Rejected: 'Rejected' } as Record<OrderFilter, string>,
  noOrders: (filter: OrderFilter) => `No ${filter.toLowerCase()} orders.`,
  /** Kanban columns, by order status. */
  columns: { Submitted: 'New', Accepted: 'Accepted', Preparing: 'Preparing', Ready: 'Ready' } as Record<string, string>,
  /** Buttons that move an order on, by the status they move it to. */
  actions: { Accepted: 'Accept', Preparing: 'Start preparing', Ready: 'Mark ready', Completed: 'Mark served' } as Record<string, string>,
  nothingHere: 'Nothing here',
  justNow: 'just now',
  minutesAgo: (minutes: number) => `${minutes} min ago`,
  note: (note: string) => `Note: ${note}`,
  rejected: (reason: string) => `Rejected: ${reason}`,
  total: 'Total',
  reject: 'Reject',
  rejectTitle: (number: number) => `Reject order #${number}?`,
  rejectHint: 'The guest sees this reason on their phone.',
  /** Sent to the guest as typed, so they are in the language the staff member is using. */
  rejectReasons: ['An item is sold out', 'The kitchen is closing', 'Duplicate order', 'Please order at the counter'],
  otherReason: 'Or type another reason',
  rejectOrder: 'Reject order'
};

export const ORDERS: Lexicon<typeof en> = {
  en,
  ar: {
    title: 'الطلبات',
    intro: 'تظهر الطلبات الجديدة هنا فوراً مع تنبيه صوتي. انقر في أي مكان مرة واحدة للسماح بالصوت.',
    filters: { Active: 'النشطة', New: 'الجديدة', Completed: 'المكتملة', Rejected: 'المرفوضة' },
    noOrders: (filter: OrderFilter) =>
      `لا توجد طلبات ${({ Active: 'نشطة', New: 'جديدة', Completed: 'مكتملة', Rejected: 'مرفوضة' } as const)[filter]}.`,
    columns: { Submitted: 'جديد', Accepted: 'مقبول', Preparing: 'قيد التحضير', Ready: 'جاهز' },
    actions: { Accepted: 'قبول', Preparing: 'بدء التحضير', Ready: 'تحديد كجاهز', Completed: 'تأكيد التقديم' },
    nothingHere: 'لا توجد طلبات',
    justNow: 'الآن',
    minutesAgo: (minutes: number) =>
      minutes === 1 ? 'منذ دقيقة' : minutes === 2 ? 'منذ دقيقتين' : minutes <= 10 ? `منذ ${minutes} دقائق` : `منذ ${minutes} دقيقة`,
    note: (note: string) => `ملاحظة: ${note}`,
    rejected: (reason: string) => `سبب الرفض: ${reason}`,
    total: 'المجموع',
    reject: 'رفض',
    rejectTitle: (number: number) => `رفض الطلب #${number}؟`,
    rejectHint: 'سيظهر هذا السبب للضيف على هاتفه.',
    rejectReasons: ['أحد الأصناف نفد', 'المطبخ على وشك الإغلاق', 'طلب مكرر', 'يرجى الطلب من الكاونتر'],
    otherReason: 'أو اكتب سبباً آخر',
    rejectOrder: 'رفض الطلب'
  }
};
