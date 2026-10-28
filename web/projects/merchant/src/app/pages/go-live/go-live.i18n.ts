import { Lexicon } from '../../core/i18n';

/** A check's title and what to do about it; count is the number behind it when there is one. */
type CheckText = { title: string; hint: (count: number) => string };

const goLive = {
  title: 'Go-live',
  intro: "What's left before guests at this venue pay with real cards. Each check ticks itself as it happens: nothing here is ticked by hand.",
  progress: (done: number, total: number) => `${done} of ${total} required checks done`,
  ready: 'Ready for guests. Every required check is done.',
  testMode: (provider: string) => `Payments are in test mode (${provider}): no card is charged yet.`,
  liveMode: (provider: string) => `Payments are live (${provider}).`,
  required: 'Required',
  recommended: 'Recommended',
  done: 'Done',
  todo: 'To do',
  open: 'Open',
  refresh: 'Check again',
  groups: {
    venue: 'Venue', tables: 'Tables and QR codes', menu: 'Menu', team: 'Team', payments: 'Payments (Mastercard Gateway)', alerts: 'Alerts and reports', pos: 'POS'
  } as Partial<Record<string, string>>,
  checks: {
    'venue.tax': { title: 'Sales tax set', hint: () => "Jordan's general sales tax is 16%. Set it, and the service charge, under Settings." },
    'venue.branding': { title: 'Logo added', hint: () => 'Guests see it on the menu and the payment page.' },
    'venue.reviews': { title: 'Public review link', hint: () => 'Happy guests are sent to your Google page to leave a review.' },
    'tables.qr': { title: 'Tables have QR codes', hint: n => `${n} active QR ${n === 1 ? 'code' : 'codes'}. Print them from Tables.` },
    'tables.scanned': { title: 'A printed QR code was scanned', hint: () => 'Scan one of the printed codes on a table with a phone, to check the print and the link.' },
    'menu.published': { title: 'A menu is published', hint: n => `${n} ${n === 1 ? 'item' : 'items'} guests can order.` },
    'menu.photos': { title: 'Most items have photos', hint: n => `${n} with a photo. Photos sell more.` },
    'team.staff': { title: 'Waiters can sign in', hint: n => `${n} ${n === 1 ? 'person' : 'people'} besides owners. Add them under Settings → Team.` },
    'payments.provider': { title: 'Mastercard Gateway connected', hint: () => 'Set up on the server with your acquirer\'s merchant id and API password (deploy/README.md).' },
    'payments.test_payment': { title: 'Test payment succeeded', hint: () => "Scan a table, order and pay with one of the acquirer's test cards." },
    'payments.webhook': { title: "Gateway's notification received", hint: () => 'Proves the webhook URL and secret are set in Merchant Administration.' },
    'payments.refund': { title: 'A refund went through', hint: () => 'Refund the test payment from Transactions.' },
    'payments.receipt': { title: 'A receipt e-mail was delivered', hint: () => 'Ask for the receipt on the payment page; it checks the e-mail settings.' },
    'payments.settlement': { title: 'Settlement report imported', hint: () => "Import the acquirer's settlement file for the test day under Reports → Settlements." },
    'payments.live': { title: 'Switched to the live gateway', hint: () => 'Last step, once the acquirer signs off the tests: the live gateway URL and merchant id.' },
    'alerts.whatsapp': { title: 'WhatsApp payment alerts', hint: n => `${n} ${n === 1 ? 'number' : 'numbers'} get a message for every payment.` },
    'alerts.reports': { title: 'Daily e-mail report', hint: () => 'The day\'s figures in your inbox every morning.' },
    'pos.connected': { title: 'POS connected', hint: () => 'Orders and payments go into your POS by themselves.' }
  } as Partial<Record<string, CheckText>>
};

export const GO_LIVE: Lexicon<typeof goLive> = {
  en: goLive,
  ar: {
    title: 'جاهزية الإطلاق',
    intro: 'ما تبقّى قبل أن يدفع الضيوف في هذا الفرع ببطاقات حقيقية. يُعلَّم كل بند تلقائياً عند حدوثه، ولا يُعلَّم أي شيء هنا يدوياً.',
    progress: (done: number, total: number) => `تم إنجاز ${done} من ${total} من البنود المطلوبة`,
    ready: 'جاهز لاستقبال الضيوف. تم إنجاز جميع البنود المطلوبة.',
    testMode: (provider: string) => `المدفوعات في وضع الاختبار (${provider})، ولا تُخصم أي مبالغ من البطاقات بعد.`,
    liveMode: (provider: string) => `المدفوعات مفعّلة فعلياً (${provider}).`,
    required: 'مطلوب',
    recommended: 'موصى به',
    done: 'تم',
    todo: 'مطلوب إنجازه',
    open: 'فتح',
    refresh: 'تحقّق مجدداً',
    groups: {
      venue: 'الفرع', tables: 'الطاولات ورموز QR', menu: 'القائمة', team: 'الفريق', payments: 'المدفوعات (Mastercard Gateway)', alerts: 'التنبيهات والتقارير', pos: 'نظام نقاط البيع (POS)'
    },
    checks: {
      'venue.tax': { title: 'تم ضبط ضريبة المبيعات', hint: () => 'ضريبة المبيعات العامة في الأردن 16%. اضبطها مع رسوم الخدمة من الإعدادات.' },
      'venue.branding': { title: 'تمت إضافة الشعار', hint: () => 'يراه الضيوف في القائمة وصفحة الدفع.' },
      'venue.reviews': { title: 'رابط التقييم العام', hint: () => 'يُوجَّه الضيوف الراضون إلى صفحتك على Google لكتابة تقييم.' },
      'tables.qr': { title: 'للطاولات رموز QR', hint: n => `${n} رمز QR فعّال. اطبعها من صفحة الطاولات.` },
      'tables.scanned': { title: 'تم مسح رمز QR مطبوع', hint: () => 'امسح أحد الرموز المطبوعة على طاولة بالهاتف للتأكد من الطباعة والرابط.' },
      'menu.published': { title: 'تم نشر قائمة', hint: n => `${n} صنف يمكن للضيوف طلبه.` },
      'menu.photos': { title: 'لمعظم الأصناف صور', hint: n => `${n} صنف له صورة. الصور تزيد المبيعات.` },
      'team.staff': { title: 'يمكن للموظفين تسجيل الدخول', hint: n => `${n} من غير المالكين. أضفهم من الإعدادات ← الفريق.` },
      'payments.provider': { title: 'تم ربط Mastercard Gateway', hint: () => 'يُضبط على الخادم برقم التاجر وكلمة مرور API من البنك المحصّل (deploy/README.md).' },
      'payments.test_payment': { title: 'نجحت دفعة تجريبية', hint: () => 'امسح رمز طاولة واطلب وادفع بإحدى بطاقات الاختبار من البنك المحصّل.' },
      'payments.webhook': { title: 'وصل إشعار البوابة', hint: () => 'يؤكد ضبط رابط الإشعارات (Webhook) والمفتاح السري في Merchant Administration.' },
      'payments.refund': { title: 'تمّ استرداد مبلغ', hint: () => 'استرد الدفعة التجريبية من صفحة المعاملات.' },
      'payments.receipt': { title: 'وصل إيصال بالبريد الإلكتروني', hint: () => 'اطلب الإيصال من صفحة الدفع، فذلك يختبر إعدادات البريد الإلكتروني.' },
      'payments.settlement': { title: 'تم استيراد تقرير التسوية', hint: () => 'استورد ملف التسوية من البنك المحصّل ليوم الاختبار من التقارير ← التسويات.' },
      'payments.live': { title: 'تم التحويل إلى البوابة الفعلية', hint: () => 'الخطوة الأخيرة بعد موافقة البنك المحصّل على الاختبارات: رابط البوابة الفعلية ورقم التاجر.' },
      'alerts.whatsapp': { title: 'تنبيهات الدفع عبر واتساب', hint: n => `${n} رقم يصله تنبيه بكل دفعة.` },
      'alerts.reports': { title: 'تقرير يومي بالبريد الإلكتروني', hint: () => 'أرقام اليوم في بريدك كل صباح.' },
      'pos.connected': { title: 'تم ربط نظام نقاط البيع', hint: () => 'تصل الطلبات والمدفوعات إلى نظام نقاط البيع تلقائياً.' }
    }
  }
};
