import { OrderFilter, OrderPeriod, OrderStatus } from '../../core/api';
import { Lexicon } from '../../core/i18n';

const en = {
  title: 'Orders',
  intro: 'New orders appear here instantly, with a chime. Click anywhere once to allow the sound. Served orders move to Closed once the table has paid in full.',
  filters: { Active: 'Active', New: 'New', Ready: 'Ready', Served: 'Served', Closed: 'Closed', Rejected: 'Rejected', All: 'All' } as Record<OrderFilter, string>,
  noOrders: (filter: OrderFilter) => (filter === 'All' ? 'No orders yet.' : `No ${filter.toLowerCase()} orders.`),
  /** Kanban columns. */
  columns: { New: 'New', Ready: 'Ready', Served: 'Served' } as Record<string, string>,
  /** Each order's stage, shown on its card when the list mixes them. */
  statuses: {
    Submitted: 'New', Accepted: 'New', Preparing: 'New', Ready: 'Ready', Completed: 'Served', Closed: 'Closed', Rejected: 'Rejected'
  } as Record<OrderStatus, string>,
  /** Buttons that move an order on, by the status they move it to. */
  actions: { Ready: 'Mark ready', Completed: 'Mark served' } as Record<string, string>,
  nothingHere: 'Nothing here',
  /** A table's later order: it carries the table's order number, as round 2, 3… */
  round: (round: number) => `Round ${round}`,
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
  rejectOrder: 'Reject order',
  // Filters
  filterOrders: 'Filter orders',
  period: 'Period',
  periods: { All: 'Any time', Day: 'Day', Week: 'Week', Month: 'Month', Quarter: 'Quarter', Year: 'Year' } as Record<OrderPeriod, string>,
  previousPeriod: 'Previous period',
  nextPeriod: 'Next period',
  previousArrow: '‹',
  nextArrow: '›',
  quarter: (q: number, year: number) => `Q${q} ${year}`,
  table: 'Table',
  allTables: 'All tables',
  orderNumber: 'Order #',
  orderNumberLabel: 'Order number',
  clearFilters: 'Clear filters',
  noMatches: 'No orders match these filters.',
  searchAll: 'Search every stage',
  showMore: 'Show more'
};

export const ORDERS: Lexicon<typeof en> = {
  en,
  ar: {
    title: 'الطلبات',
    intro: 'تظهر الطلبات الجديدة هنا فوراً مع تنبيه صوتي. انقر في أي مكان مرة واحدة للسماح بالصوت. تنتقل الطلبات المُقدَّمة إلى المغلقة بعد أن تدفع الطاولة الفاتورة كاملة.',
    filters: { Active: 'النشطة', New: 'الجديدة', Ready: 'الجاهزة', Served: 'المُقدَّمة', Closed: 'المغلقة', Rejected: 'المرفوضة', All: 'الكل' },
    noOrders: (filter: OrderFilter) =>
      filter === 'All'
        ? 'لا توجد طلبات بعد.'
        : `لا توجد طلبات ${({ Active: 'نشطة', New: 'جديدة', Ready: 'جاهزة', Served: 'مُقدَّمة', Closed: 'مغلقة', Rejected: 'مرفوضة' } as const)[filter]}.`,
    columns: { New: 'جديد', Ready: 'جاهز', Served: 'تم التقديم' },
    statuses: {
      Submitted: 'جديد', Accepted: 'جديد', Preparing: 'جديد', Ready: 'جاهز', Completed: 'تم التقديم', Closed: 'مغلق', Rejected: 'مرفوض'
    },
    actions: { Ready: 'تحديد كجاهز', Completed: 'تأكيد التقديم' },
    nothingHere: 'لا توجد طلبات',
    round: (round: number) => `الجولة ${round}`,
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
    rejectOrder: 'رفض الطلب',
    filterOrders: 'تصفية الطلبات',
    period: 'الفترة',
    periods: { All: 'كل الأوقات', Day: 'يوم', Week: 'أسبوع', Month: 'شهر', Quarter: 'ربع سنة', Year: 'سنة' },
    previousPeriod: 'الفترة السابقة',
    nextPeriod: 'الفترة التالية',
    previousArrow: '›',
    nextArrow: '‹',
    quarter: (q: number, year: number) => `الربع ${q} ${year}`,
    table: 'الطاولة',
    allTables: 'كل الطاولات',
    orderNumber: 'رقم الطلب',
    orderNumberLabel: 'رقم الطلب',
    clearFilters: 'مسح التصفية',
    noMatches: 'لا توجد طلبات تطابق هذه التصفية.',
    searchAll: 'البحث في كل المراحل',
    showMore: 'عرض المزيد'
  }
};
