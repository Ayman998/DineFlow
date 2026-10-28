import { Lexicon } from '../../core/i18n';

/**
 * An Arabic count: the singular and dual stand alone ("سطر واحد", "سطران"); 3–10 take the
 * plural ("3 أسطر"); 0, 11 and up take the accusative singular ("15 سطراً").
 */
function arCount(n: number, one: string, two: string, few: string, many: string): string {
  if (n === 1) return one;
  if (n === 2) return two;
  const rest = n % 100;
  return rest >= 3 && rest <= 10 ? `${n} ${few}` : `${n} ${many}`;
}

// ── Reports page: tabs and scheduled e-mail reports ─────────────────────────────────────────

const reportsEn = {
  title: 'Reports',
  intro: "Whether every payment reached your bank, the acquirer's settlements, and the figures by e-mail.",
  tabs: { reconciliation: 'Reconciliation', settlements: 'Settlements', email: 'E-mail reports' },
  emailOff: 'E-mail reports are switched off for this environment.',
  emailReports: 'E-mail reports',
  newEmailReport: 'New e-mail report',
  noSchedules: 'No reports scheduled. Add one to get the figures in your inbox without opening the dashboard.',
  frequencies: { Daily: 'Daily', Weekly: 'Weekly', Monthly: 'Monthly' } as Record<string, string>,
  reportName: { Daily: 'Daily report', Weekly: 'Weekly report', Monthly: 'Monthly report' } as Record<string, string>,
  describe: {
    Daily: (at: string) => `Every day at ${at}, for the day before`,
    Weekly: (at: string) => `Mondays at ${at}, for the week before`,
    Monthly: (at: string) => `On the 1st at ${at}, for the month before`
  } as Record<string, (at: string) => string>,
  paused: 'Paused',
  to: (recipients: string) => `To ${recipients}`,
  next: (when: string) => `Next ${when}`,
  lastSent: (when: string) => `last sent ${when}`,
  sendNow: 'Send now',
  onlyManagers: 'Only owners and managers can change who gets reports.',
  confirmStop: (report: string, recipients: string) => `Stop the ${report.toLowerCase()} to ${recipients}?`,
  queued: (count: number) => `Queued for ${count} ${count === 1 ? 'recipient' : 'recipients'}. It goes out within a few seconds.`,
  // Preview
  preview: 'Preview',
  comparedWith: (period: string, frequency: string) =>
    `${period}, compared with the ${frequency === 'Daily' ? 'day' : frequency === 'Weekly' ? 'week' : 'month'} before`,
  reportPeriod: 'Report period',
  figure: 'Figure',
  thisPeriod: 'This period',
  before: 'Before',
  change: 'Change',
  /** The API's metric labels are English; these follow ReportBuilder's metric keys. */
  metrics: {
    revenue: 'Revenue',
    tips: 'Tips',
    refunds: 'Refunds',
    net: 'Net (revenue + tips − refunds)',
    orders: 'Orders',
    payments: 'Successful payments',
    average: 'Average payment',
    failures: 'Payment failures',
    links: 'Paid through payment links',
    reviews: 'Ratings',
    rating: 'Average rating'
  } as Record<string, string>,
  bestSellers: 'Best sellers',
  emailed: 'E-mailed',
  // Form
  howOften: 'How often',
  dailyOption: 'Daily, for the day before',
  weeklyOption: 'Weekly on Monday, for the week before',
  monthlyOption: 'Monthly on the 1st, for the month before',
  sendAt: 'Send at (venue time)',
  sendTo: 'Send to',
  recipientsHint: 'Up to 10 addresses, separated by commas.',
  sendOnSchedule: 'Send on schedule',
  recipientsRequired: 'Add at least one e-mail address.'
};

export const REPORTS: Lexicon<typeof reportsEn> = {
  en: reportsEn,
  ar: {
    title: 'التقارير',
    intro: 'هل وصلت كل دفعة إلى حسابك البنكي، وتسويات البنك المستحوذ، والأرقام عبر البريد الإلكتروني.',
    tabs: { reconciliation: 'المطابقة', settlements: 'التسويات', email: 'تقارير البريد الإلكتروني' },
    emailOff: 'تقارير البريد الإلكتروني معطّلة في هذه البيئة.',
    emailReports: 'تقارير البريد الإلكتروني',
    newEmailReport: 'تقرير بريد إلكتروني جديد',
    noSchedules: 'لا توجد تقارير مجدولة. أضف تقريراً لتصلك الأرقام إلى بريدك دون فتح لوحة التحكم.',
    frequencies: { Daily: 'يومي', Weekly: 'أسبوعي', Monthly: 'شهري' },
    reportName: { Daily: 'التقرير اليومي', Weekly: 'التقرير الأسبوعي', Monthly: 'التقرير الشهري' },
    describe: {
      Daily: (at: string) => `يومياً الساعة ${at}، عن اليوم السابق`,
      Weekly: (at: string) => `كل يوم اثنين الساعة ${at}، عن الأسبوع السابق`,
      Monthly: (at: string) => `في اليوم الأول من كل شهر الساعة ${at}، عن الشهر السابق`
    },
    paused: 'متوقف مؤقتاً',
    to: (recipients: string) => `إلى ${recipients}`,
    next: (when: string) => `الإرسال التالي ${when}`,
    lastSent: (when: string) => `آخر إرسال ${when}`,
    sendNow: 'أرسل الآن',
    onlyManagers: 'يمكن للمالكين والمديرين فقط تغيير من يستلم التقارير.',
    confirmStop: (report: string, recipients: string) => `إيقاف ${report} المُرسَل إلى ${recipients}؟`,
    queued: (count: number) =>
      `أُضيف إلى قائمة الإرسال لـ${arCount(count, 'مستلم واحد', 'مستلمَين', 'مستلمين', 'مستلماً')}. سيُرسَل خلال ثوانٍ.`,
    preview: 'معاينة',
    comparedWith: (period: string, frequency: string) =>
      `${period}، مقارنةً ${frequency === 'Daily' ? 'باليوم' : frequency === 'Weekly' ? 'بالأسبوع' : 'بالشهر'} السابق`,
    reportPeriod: 'فترة التقرير',
    figure: 'المؤشر',
    thisPeriod: 'هذه الفترة',
    before: 'الفترة السابقة',
    change: 'التغيّر',
    metrics: {
      revenue: 'الإيرادات',
      tips: 'الإكراميات',
      refunds: 'الاستردادات',
      net: 'الصافي (الإيرادات + الإكراميات − الاستردادات)',
      orders: 'الطلبات',
      payments: 'الدفعات الناجحة',
      average: 'متوسط الدفعة',
      failures: 'الدفعات الفاشلة',
      links: 'المدفوع عبر روابط الدفع',
      reviews: 'التقييمات',
      rating: 'متوسط التقييم'
    },
    bestSellers: 'الأكثر مبيعاً',
    emailed: 'المُرسَل بالبريد الإلكتروني',
    howOften: 'التكرار',
    dailyOption: 'يومي، عن اليوم السابق',
    weeklyOption: 'أسبوعي يوم الاثنين، عن الأسبوع السابق',
    monthlyOption: 'شهري في اليوم الأول، عن الشهر السابق',
    sendAt: 'وقت الإرسال (بتوقيت الفرع)',
    sendTo: 'إرسال إلى',
    recipientsHint: 'حتى 10 عناوين، مفصولة بفواصل.',
    sendOnSchedule: 'الإرسال حسب الجدولة',
    recipientsRequired: 'أضف عنوان بريد إلكتروني واحداً على الأقل.'
  }
};

// ── Settlements panel ───────────────────────────────────────────────────────────────────────

const settlementsEn = {
  fileTooLarge: 'That file is over 5 MB. Export a shorter period.',
  imported: (fileName: string) => `Imported ${fileName}`,
  simulated: 'Simulated a payout',
  result: (what: string, batches: string, lines: number, differences: number) =>
    `${what}: ${batches}, ${lines} ${lines === 1 ? 'line' : 'lines'}. ` +
    (differences
      ? `${differences} ${differences === 1 ? 'difference' : 'differences'} to look at on the Reconciliation tab.`
      : 'Everything matched.'),
  importTitle: 'Import a settlement report',
  importIntro:
    "Download the settlement or payout report from your acquirer's portal as CSV and drop it here. DineFlow matches every line to a payment and reconciles the days it covers. Importing the same batch twice is refused.",
  simulateHint: 'Settles every unsettled test payment, as an acquirer would',
  simulate: 'Simulate a payout',
  importing: 'Importing…',
  chooseFile: 'Choose CSV file',
  whichColumns: 'Which columns does the file need?',
  helpIntro: 'One row per transaction, with a header row. Commas or semicolons both work; column names are matched loosely.',
  help: {
    and: 'and',
    or: 'or',
    comma: ',',
    batch: 'Batch',
    batchColumns: '(batch, payout id, settlement id)',
    payoutDate: 'payout date',
    payoutDateColumns: '(payout, settlement or value date)',
    transactionDate: 'Transaction date',
    type: 'type',
    typeValues: ': sale, refund or fee',
    orderId: 'Order id',
    orderIdNote: "(the gateway's order id)",
    reference: 'reference',
    referenceNote: "(DineFlow's payment reference)",
    amount: 'Amount',
    amountIn: (currency: string) => `in ${currency}, e.g. 12.500, and optionally`,
    fee: 'fee',
    refundId: 'refund id',
    currency: 'currency'
  },
  settledSales: 'Settled sales',
  refunds: 'Refunds',
  acquirerFees: 'Acquirer fees',
  paidOut: 'Paid out',
  settlements: 'Settlements',
  noSettlements: "No settlements yet. Import your acquirer's first settlement report.",
  payout: 'Payout',
  batch: 'Batch',
  lines: 'Lines',
  sales: 'Sales',
  fees: 'Fees',
  net: 'Net',
  statuses: { Expected: 'Expected', Paid: 'Paid' } as Record<string, string>,
  date: 'Date',
  type: 'Type',
  acquirerOrder: 'Acquirer order',
  dineflowPayment: 'DineFlow payment',
  amount: 'Amount',
  fee: 'Fee',
  lineKinds: { Payment: 'Sale', Refund: 'Refund', Adjustment: 'Adjustment' } as Record<string, string>,
  notMatched: 'Not matched'
};

export const SETTLEMENTS: Lexicon<typeof settlementsEn> = {
  en: settlementsEn,
  ar: {
    fileTooLarge: 'حجم الملف يتجاوز 5 ميغابايت. صدّر فترة أقصر.',
    imported: (fileName: string) => `تم استيراد ${fileName}`,
    simulated: 'تمت محاكاة دفعة محوّلة',
    result: (what: string, batches: string, lines: number, differences: number) =>
      `${what}: ${batches}، ${arCount(lines, 'سطر واحد', 'سطران', 'أسطر', 'سطراً')}. ` +
      (differences
        ? `${arCount(differences, 'فرق واحد بحاجة', 'فرقان بحاجة', 'فروقات بحاجة', 'فرقاً بحاجة')} إلى مراجعة في تبويب المطابقة.`
        : 'تطابق كل شيء.'),
    importTitle: 'استيراد تقرير تسوية',
    importIntro:
      'نزّل تقرير التسوية أو الدفعات المحوّلة من بوابة البنك المستحوذ بصيغة CSV وأفلته هنا. يطابق DineFlow كل سطر مع دفعة ويُجري المطابقة للأيام التي يغطيها. يُرفض استيراد الحزمة نفسها مرتين.',
    simulateHint: 'يسوّي كل دفعة تجريبية غير مسوّاة، كما يفعل البنك المستحوذ',
    simulate: 'محاكاة دفعة محوّلة',
    importing: 'جارٍ الاستيراد…',
    chooseFile: 'اختر ملف CSV',
    whichColumns: 'ما الأعمدة المطلوبة في الملف؟',
    helpIntro: 'صف واحد لكل معاملة، مع صف للعناوين. تصلح الفواصل أو الفواصل المنقوطة، وتُطابَق أسماء الأعمدة بمرونة.',
    help: {
      and: 'و',
      or: 'أو',
      comma: '،',
      batch: 'الحزمة',
      batchColumns: '(batch أو payout id أو settlement id)',
      payoutDate: 'تاريخ التحويل',
      payoutDateColumns: '(payout أو settlement أو value date)',
      transactionDate: 'تاريخ المعاملة',
      type: 'النوع',
      typeValues: ': بيع (sale) أو استرداد (refund) أو رسوم (fee)',
      orderId: 'رقم الطلب',
      orderIdNote: '(رقم الطلب لدى بوابة الدفع)',
      reference: 'المرجع',
      referenceNote: '(مرجع الدفعة في DineFlow)',
      amount: 'المبلغ',
      amountIn: (currency: string) => `بعملة ${currency}، مثل 12.500، واختيارياً`,
      fee: 'الرسوم (fee)',
      refundId: 'رقم الاسترداد (refund id)',
      currency: 'العملة (currency)'
    },
    settledSales: 'المبيعات المسوّاة',
    refunds: 'الاستردادات',
    acquirerFees: 'رسوم البنك المستحوذ',
    paidOut: 'المبلغ المحوّل',
    settlements: 'التسويات',
    noSettlements: 'لا توجد تسويات بعد. استورد أول تقرير تسوية من البنك المستحوذ.',
    payout: 'الدفعة المحوّلة',
    batch: 'الحزمة',
    lines: 'الأسطر',
    sales: 'المبيعات',
    fees: 'الرسوم',
    net: 'الصافي',
    statuses: { Expected: 'متوقعة', Paid: 'مدفوعة' },
    date: 'التاريخ',
    type: 'النوع',
    acquirerOrder: 'طلب البنك المستحوذ',
    dineflowPayment: 'دفعة DineFlow',
    amount: 'المبلغ',
    fee: 'الرسوم',
    lineKinds: { Payment: 'بيع', Refund: 'استرداد', Adjustment: 'تعديل' },
    notMatched: 'غير مطابقة'
  }
};

// ── Reconciliation panel ────────────────────────────────────────────────────────────────────

/** A difference's values for its wording: amounts and the day already formatted. */
export interface DifferenceValues {
  reference: string;
  expected: string;
  actual: string;
  day: string;
  /** How often the acquirer settled it (settled_twice); 0 when unknown. */
  count: number;
  /** pos_waiting: tries so far. */
  attempts: number;
  /** pos_refused / pos_waiting: the POS's own message, as it sent it. */
  error: string | null;
}

const reconciliationEn = {
  kinds: {
    NotSettled: 'Not settled',
    UnknownToDineFlow: 'Unknown to DineFlow',
    AmountMismatch: 'Amount differs',
    RefundMismatch: 'Refund differs',
    NotInPos: 'Not in POS',
    SettledTwice: 'Settled twice'
  } as Record<string, string>,
  statuses: { Open: 'Open', Cleared: 'Cleared', Resolved: 'Resolved' } as Record<string, string>,
  /** What a difference is about, by the API's detailsCode. */
  details: {
    not_settled: (d: DifferenceValues) => `${d.reference} (${d.expected}) was paid on ${d.day} but isn't in any settlement from the acquirer.`,
    settled_twice: (d: DifferenceValues) =>
      d.count > 1 ? `${d.reference} appears ${d.count} times in settlements.` : `${d.reference} appears more than once in settlements.`,
    amount_mismatch: (d: DifferenceValues) => `${d.reference}: DineFlow charged ${d.expected}, the acquirer settled ${d.actual}.`,
    unknown_to_dineflow: (d: DifferenceValues) =>
      `The acquirer settled ${d.actual} for ${d.reference}, which DineFlow has no successful payment for.`,
    refund_missing: (d: DifferenceValues) => `A refund of ${d.expected} on ${d.reference} isn't in any settlement.`,
    refund_amount: (d: DifferenceValues) =>
      `Refund on ${d.reference}: DineFlow refunded ${d.expected}, the acquirer took back ${d.actual}.`,
    refund_unknown: (d: DifferenceValues) => `The acquirer took back ${d.actual} for ${d.reference}, but DineFlow has no matching refund.`,
    pos_refused: (d: DifferenceValues) => `${d.reference} was refused by the POS: ${d.error}`,
    pos_waiting: (d: DifferenceValues) => `${d.reference} still isn't in the POS (${d.attempts} attempts): ${d.error ?? 'waiting'}`
  },
  reconciled: (day: string, open: number) => `Reconciled ${day}: ${open ? open + ' to look at' : 'everything adds up'}.`,
  noteRequired: 'Add a short note saying why this is acceptable.',
  resolved: (reference: string) => `Resolved ${reference}.`,
  // Totals
  toLookAt: 'Differences to look at',
  takenInDineFlow: 'Taken in DineFlow (30 days)',
  settledByAcquirer: 'Settled by the acquirer',
  feesAmount: (amount: string) => `Fees ${amount}`,
  awaitingSettlement: 'Awaiting settlement',
  acquirersPayLater: 'Acquirers pay a day or two later',
  // Queue
  differences: 'Differences',
  differencesIntro:
    "What doesn't add up between DineFlow, the acquirer's settlements and the POS. Most clear by themselves once the settlement arrives or the POS catches up.",
  show: 'Show',
  open: 'Open',
  nothingOpen: 'Nothing to look at. Every reconciled day adds up.',
  noDifferences: 'No differences yet.',
  resolvedBy: (who: string | null, note: string) => `Resolved by ${who ?? 'staff'}: ${note}`,
  comparison: (ours: string, theirs: string) => `DineFlow ${ours} · acquirer ${theirs}`,
  resolveEllipsis: 'Resolve…',
  notePlaceholder: "Why it's acceptable, e.g. claim #4471 raised with MEPS",
  resolve: 'Resolve',
  // Days
  days: 'Days',
  daysIntro: 'Reconciled every hour for the last week, and whenever a settlement is imported.',
  reconciling: 'Reconciling…',
  reconcileYesterday: 'Reconcile yesterday now',
  noDays: 'No days reconciled yet.',
  day: 'Day',
  payments: 'Payments',
  dineflow: 'DineFlow',
  settled: 'Settled',
  fees: 'Fees',
  refunds: 'Refunds',
  inPos: 'In POS',
  status: 'Status',
  openCount: (count: number) => `${count} to look at`,
  awaitingCount: (count: number) => `${count} awaiting settlement`,
  balanced: 'Balanced',
  noPayments: 'No payments',
  // Export
  ledgerExport: 'Ledger export',
  ledgerIntro: 'Every money movement with its payment, table or link, and the settlement batch and fee, for your accountant.',
  from: 'From',
  to: 'To',
  exportCsv: 'Export CSV'
};

export const RECONCILIATION: Lexicon<typeof reconciliationEn> = {
  en: reconciliationEn,
  ar: {
    kinds: {
      NotSettled: 'لم تُسوَّ',
      UnknownToDineFlow: 'غير معروفة لدى DineFlow',
      AmountMismatch: 'عدم تطابق المبلغ',
      RefundMismatch: 'عدم تطابق الاسترداد',
      NotInPos: 'غير موجودة في نقاط البيع',
      SettledTwice: 'سُوّيت مرتين'
    },
    statuses: { Open: 'مفتوحة', Cleared: 'تمت المطابقة', Resolved: 'تمت المعالجة' },
    details: {
      not_settled: (d: DifferenceValues) => `${d.reference} (${d.expected}) دُفعت في ${d.day} لكنها غير موجودة في أي تسوية من البنك المستحوذ.`,
      settled_twice: (d: DifferenceValues) =>
        d.count > 1
          ? `${d.reference} تظهر ${arCount(d.count, 'مرة واحدة', 'مرتين', 'مرات', 'مرة')} في التسويات.`
          : `${d.reference} تظهر أكثر من مرة في التسويات.`,
      amount_mismatch: (d: DifferenceValues) => `${d.reference}: حصّل DineFlow ${d.expected}، بينما سوّى البنك المستحوذ ${d.actual}.`,
      unknown_to_dineflow: (d: DifferenceValues) =>
        `سوّى البنك المستحوذ ${d.actual} لـ ${d.reference}، ولا توجد لدى DineFlow دفعة ناجحة مقابلها.`,
      refund_missing: (d: DifferenceValues) => `استرداد بقيمة ${d.expected} على ${d.reference} غير موجود في أي تسوية.`,
      refund_amount: (d: DifferenceValues) =>
        `استرداد على ${d.reference}: ردّ DineFlow ${d.expected}، بينما استرجع البنك المستحوذ ${d.actual}.`,
      refund_unknown: (d: DifferenceValues) => `استرجع البنك المستحوذ ${d.actual} لـ ${d.reference}، ولا يوجد لدى DineFlow استرداد مطابق.`,
      pos_refused: (d: DifferenceValues) => `رفض نظام نقاط البيع ${d.reference}: ${d.error}`,
      pos_waiting: (d: DifferenceValues) =>
        `${d.reference} لم تُسجَّل في نظام نقاط البيع بعد (${arCount(d.attempts, 'محاولة واحدة', 'محاولتان', 'محاولات', 'محاولة')}): ${d.error ?? 'بانتظار المحاولة'}`
    },
    reconciled: (day: string, open: number) => `تمت مطابقة ${day}: ${open ? `${open} بحاجة إلى مراجعة` : 'كل الأرقام متطابقة'}.`,
    noteRequired: 'أضف ملاحظة قصيرة توضّح سبب قبول ذلك.',
    resolved: (reference: string) => `تمت معالجة ${reference}.`,
    toLookAt: 'فروقات بحاجة إلى مراجعة',
    takenInDineFlow: 'المحصّل في DineFlow (آخر 30 يوماً)',
    settledByAcquirer: 'ما سوّاه البنك المستحوذ',
    feesAmount: (amount: string) => `الرسوم ${amount}`,
    awaitingSettlement: 'بانتظار التسوية',
    acquirersPayLater: 'يحوّل البنك المستحوذ المبالغ بعد يوم أو يومين',
    differences: 'الفروقات',
    differencesIntro:
      'ما لا يتطابق بين DineFlow وتسويات البنك المستحوذ ونظام نقاط البيع. يزول معظمها تلقائياً عند وصول التسوية أو عند تحديث نظام نقاط البيع.',
    show: 'عرض',
    open: 'المفتوحة',
    nothingOpen: 'لا شيء بحاجة إلى مراجعة. كل الأيام التي تمت مطابقتها متوازنة.',
    noDifferences: 'لا توجد فروقات بعد.',
    resolvedBy: (who: string | null, note: string) => `عالجها ${who ?? 'أحد الموظفين'}: ${note}`,
    comparison: (ours: string, theirs: string) => `DineFlow ${ours} · البنك المستحوذ ${theirs}`,
    resolveEllipsis: 'معالجة…',
    notePlaceholder: 'سبب القبول، مثلاً: رُفعت المطالبة #4471 لدى MEPS',
    resolve: 'معالجة',
    days: 'الأيام',
    daysIntro: 'تتم المطابقة كل ساعة لأيام الأسبوع الماضي، وعند استيراد أي تسوية.',
    reconciling: 'جارٍ المطابقة…',
    reconcileYesterday: 'مطابقة يوم أمس الآن',
    noDays: 'لم تتم مطابقة أي يوم بعد.',
    day: 'يوم العمل',
    payments: 'الدفعات',
    dineflow: 'DineFlow',
    settled: 'المسوّى',
    fees: 'الرسوم',
    refunds: 'الاستردادات',
    inPos: 'في نقاط البيع',
    status: 'الحالة',
    openCount: (count: number) => `${count} بحاجة إلى مراجعة`,
    awaitingCount: (count: number) => `${count} بانتظار التسوية`,
    balanced: 'متوازن',
    noPayments: 'لا توجد دفعات',
    ledgerExport: 'تصدير دفتر الحركات',
    ledgerIntro: 'كل حركة مالية مع دفعتها أو طاولتها أو رابطها، وحزمة التسوية والرسوم، لمحاسبك.',
    from: 'من',
    to: 'إلى',
    exportCsv: 'تصدير CSV'
  }
};
