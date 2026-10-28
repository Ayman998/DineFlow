import { Lexicon } from '../../core/i18n';

/** "7 days", "30 days" in Arabic: أيام for 3–10, يوماً from 11 on. */
function arDays(n: number): string {
  if (n === 1) return 'يوم واحد';
  if (n === 2) return 'يومان';
  return n >= 3 && n <= 10 ? `${n} أيام` : `${n} يوماً`;
}

const en = {
  subtitle: 'ratings guests left after paying',
  period: 'Period',
  lastDays: (days: number) => `Last ${days} days`,
  exporting: 'Exporting…',
  exportCsv: 'Export CSV',

  // Headline numbers
  feedbacks: 'Feedbacks',
  averageRating: 'Average rating',
  ratedShare: 'Payments that left a rating',
  publicShare: 'Guided to public review',
  perStar: 'Ratings per star',
  points: (n: number) => `${n} pts`,
  same: 'same',
  vsPrevious: (days: number) => `vs previous ${days} days`,

  // Filters
  whichReviews: 'Which reviews',
  kinds: { All: 'All', Positive: 'Positive 4–5★', Constructive: 'Constructive 1–3★' } as Record<string, string>,
  stars: 'Stars',
  starsAll: 'Stars: all',
  sort: 'Sort',
  sorts: { Recent: 'Most recent', Oldest: 'Oldest', Highest: 'Highest rating', Lowest: 'Lowest rating' } as Record<string, string>,
  onlyWithComment: 'Only with comment',

  // List
  noMatch: 'No reviews match in this period.',
  colWhen: 'Date & time',
  colRating: 'Rating',
  colReview: 'Review',
  colTable: 'Table',
  colActions: 'Actions',
  starCount: (n: number) => (n === 1 ? '1 star' : `${n} stars`),
  noComment: 'No comment',
  leftContact: 'Left contact details',
  openedPublic: 'Opened public review',
  followedUp: 'Followed up',
  orderDetails: 'Order details',
  rowsPerPage: 'Rows per page',
  range: (from: number, to: number, total: number) => `${from}–${to} of ${total}`,
  previousPage: 'Previous page',
  nextPage: 'Next page',

  // Staff
  staffRatings: 'Staff ratings',
  staffIntro: (days: number) => `Ratings for the staff member who served the table, last ${days} days`,
  noStaff: "No ratings can be credited to staff yet. They're credited when staff accept or serve orders in the queue.",
  name: 'Name',
  ratings: 'Ratings',
  average: 'Average',

  // Detail
  review: 'Review',
  positive: 'Positive',
  positiveOpened: (platform: string | null) => `Positive · opened ${platform}`,
  constructivePrivate: 'Constructive · private',
  servedBy: (name: string) => `Served by ${name}`,
  guestContact: 'Guest contact',
  email: 'Email',
  phone: 'Phone',
  contactRecorded: 'The guest agreed to be contacted about this feedback. This view has been recorded.',
  showContact: 'Show contact details',
  viewsAudited: 'Each view is recorded in the audit log.',
  askManager: 'The guest left contact details. Ask an owner or manager to follow up.',
  payment: 'Payment',
  time: 'Time',
  methodLast4: 'Method · last 4 digits',
  split: 'Split',
  byItem: 'By item',
  tip: 'Tip',
  status: 'Status',
  total: 'Total',
  /** Payment statuses, by API value; anything else is shown as it comes. */
  paymentStatuses: { Succeeded: '✓ Completed' } as Record<string, string>,
  /** Payment methods, by API value (card brands are shown as they come). */
  methods: { Card: 'Card', ApplePay: 'ApplePay', GooglePay: 'GooglePay', StcPay: 'StcPay' } as Record<string, string>,
  items: 'Items',
  item: 'Item',
  qty: 'Qty',
  price: 'Price',
  followedUpAt: (date: string, by: string | null) => `Followed up ${date}${by ? ` by ${by}` : ''}`,
  markFollowedUp: 'Mark as followed up'
};

export const REVIEWS: Lexicon<typeof en> = {
  en,
  ar: {
    subtitle: 'تقييمات الضيوف بعد الدفع',
    period: 'الفترة',
    lastDays: (days: number) => `آخر ${arDays(days)}`,
    exporting: 'جارٍ التصدير…',
    exportCsv: 'تصدير CSV',

    feedbacks: 'الملاحظات',
    averageRating: 'متوسط التقييم',
    ratedShare: 'الدفعات التي تركت تقييماً',
    publicShare: 'وُجّهت إلى تقييم عام',
    perStar: 'التقييمات حسب عدد النجوم',
    points: (n: number) => `${n} نقطة`,
    same: 'بلا تغيير',
    vsPrevious: (days: number) => `مقارنة بالفترة السابقة (${arDays(days)})`,

    whichReviews: 'أي التقييمات',
    kinds: { All: 'الكل', Positive: 'إيجابية 4–5★', Constructive: 'بنّاءة 1–3★' },
    stars: 'النجوم',
    starsAll: 'النجوم: الكل',
    sort: 'الترتيب',
    sorts: { Recent: 'الأحدث', Oldest: 'الأقدم', Highest: 'الأعلى تقييماً', Lowest: 'الأقل تقييماً' },
    onlyWithComment: 'مع تعليق فقط',

    noMatch: 'لا توجد تقييمات مطابقة في هذه الفترة.',
    colWhen: 'التاريخ والوقت',
    colRating: 'التقييم',
    colReview: 'التعليق',
    colTable: 'الطاولة',
    colActions: 'الإجراءات',
    starCount: (n: number) => (n === 1 ? 'نجمة واحدة' : n === 2 ? 'نجمتان' : `${n} نجوم`),
    noComment: 'بدون تعليق',
    leftContact: 'ترك بيانات التواصل',
    openedPublic: 'فتح صفحة التقييم العام',
    followedUp: 'تمت المتابعة',
    orderDetails: 'تفاصيل الطلب',
    rowsPerPage: 'عدد الصفوف في الصفحة',
    range: (from: number, to: number, total: number) => `${from}–${to} من ${total}`,
    previousPage: 'الصفحة السابقة',
    nextPage: 'الصفحة التالية',

    staffRatings: 'تقييمات الموظفين',
    staffIntro: (days: number) => `تقييمات الموظف الذي خدم الطاولة، آخر ${arDays(days)}`,
    noStaff: 'لا توجد تقييمات يمكن نسبها إلى الموظفين بعد. تُنسب التقييمات عندما يقبل الموظفون الطلبات أو يقدّمونها من قائمة الطلبات.',
    name: 'الاسم',
    ratings: 'التقييمات',
    average: 'المتوسط',

    review: 'تقييم',
    positive: 'إيجابي',
    positiveOpened: (platform: string | null) => `إيجابي · فتح ${platform}`,
    constructivePrivate: 'بنّاء · خاص',
    servedBy: (name: string) => `قدّم الخدمة: ${name}`,
    guestContact: 'بيانات تواصل الضيف',
    email: 'البريد الإلكتروني',
    phone: 'الهاتف',
    contactRecorded: 'وافق الضيف على التواصل معه بخصوص هذه الملاحظات. تم تسجيل هذا العرض.',
    showContact: 'عرض بيانات التواصل',
    viewsAudited: 'يُسجَّل كل عرض في سجل التدقيق.',
    askManager: 'ترك الضيف بيانات التواصل. اطلب من المالك أو المدير المتابعة.',
    payment: 'الدفعة',
    time: 'الوقت',
    methodLast4: 'طريقة الدفع · آخر 4 أرقام',
    split: 'التقسيم',
    byItem: 'حسب الصنف',
    tip: 'الإكرامية',
    status: 'الحالة',
    total: 'الإجمالي',
    paymentStatuses: {
      Succeeded: '✓ مكتملة', Created: 'تم الإنشاء', RequiresAction: 'بانتظار الضيف', Processing: 'قيد المعالجة', Failed: 'مرفوضة', Cancelled: 'ملغاة'
    },
    methods: { Card: 'بطاقة', ApplePay: 'Apple Pay', GooglePay: 'Google Pay', StcPay: 'STC Pay' },
    items: 'الأصناف',
    item: 'الصنف',
    qty: 'الكمية',
    price: 'السعر',
    followedUpAt: (date: string, by: string | null) => `تمت المتابعة ${date}${by ? ` بواسطة ${by}` : ''}`,
    markFollowedUp: 'تحديد كمُتابَع'
  }
};
