import { HttpInterceptorFn } from '@angular/common/http';
import { DOCUMENT, Injectable, Signal, computed, effect, inject, signal } from '@angular/core';

export type Lang = 'en' | 'ar';

/**
 * A page's strings in English and Arabic. Each page keeps its own next to its component
 * (e.g. tables.i18n.ts) and reads them with <c>inject(I18n).use(TABLES)</c>; the Arabic must
 * have the same keys as the English, so a missing translation is a build error.
 */
export interface Lexicon<T> {
  en: T;
  ar: NoInfer<T>;
}

/** Strings used across the portal: the shell, navigation, statuses and generic messages. */
const EN = {
  languageName: 'English',
  otherLanguage: 'العربية',
  changeLanguage: 'Change language',
  nav: {
    overview: 'Overview', orders: 'Orders', tables: 'Tables', transactions: 'Transactions', menu: 'Menu',
    'item-categories': 'Item Categories', items: 'Items',
    promotions: 'Promotions', 'payment-links': 'Payment Links', reviews: 'Reviews', reports: 'Reports', settings: 'Settings'
  } as Record<string, string>,
  newOrdersBadge: (count: number) => `${count} new orders`,
  live: 'Live',
  offline: 'Offline: reload to reconnect',
  connecting: 'Connecting…',
  signOut: 'Sign out',
  dismiss: 'Dismiss',
  roles: { Owner: 'Owner', Manager: 'Manager', Staff: 'Staff', Admin: 'Admin' } as Record<string, string>,
  locationTypes: { Table: 'Table', Counter: 'Counter', HotelRoom: 'Room', PoolBeach: 'Pool/Beach' } as Record<string, string>,
  noArea: 'No area',
  // Toasts
  newOrder: (number: number | string) => `New order #${number}`,
  paymentReceived: 'Payment received',
  billPaid: 'Bill fully paid',
  tip: (amount: string) => `tip ${amount}`,
  sharesOf: (shares: number, people: number) => `${shares} of ${people} shares`,
  items: (count: number) => (count === 1 ? '1 item' : `${count} items`),
  customAmount: 'Custom amount',
  newReview: 'New review',
  refundUpdate: 'Refund update',
  linkPaid: 'Payment link paid',
  view: 'View',
  openOrders: 'Open orders',
  openTransactions: 'Open transactions',
  openPaymentLinks: 'Open payment links',
  openReviews: 'Open reviews',
  refundConfirmed: 'Refund confirmed',
  refundDeclined: 'Refund declined',
  ref: (reference: string) => `Ref ${reference}`,
  paidInFull: 'Paid in full',
  stillToPay: (amount: string) => `${amount} still to pay`,
  stars: (count: number) => `${count} stars`,
  withComment: 'with a comment',
  signIn: 'Sign in',
  // Generic
  loading: 'Loading…',
  save: 'Save',
  saving: 'Saving…',
  cancel: 'Cancel',
  close: 'Close',
  edit: 'Edit',
  delete: 'Delete',
  add: 'Add',
  search: 'Search',
  all: 'All',
  yes: 'Yes',
  no: 'No',
  none: 'None',
  retry: 'Try again',
  comingSoon: (sprint: number) => `Coming in sprint ${sprint}.`,
  // Errors that don't come from the API
  apiUnreachable: "Can't reach the DineFlow API. Is it running?",
  forbidden: "Your role can't make this change. Ask an owner or manager.",
  requestFailed: (status: number) => `Request failed (${status}).`,
  somethingWrong: 'Something went wrong.',
  noVenue: 'This account has no venue yet.'
};

type Common = typeof EN;

const AR: Common = {
  languageName: 'العربية',
  otherLanguage: 'English',
  changeLanguage: 'تغيير اللغة',
  nav: {
    overview: 'نظرة عامة', orders: 'الطلبات', tables: 'الطاولات', transactions: 'المعاملات', menu: 'القائمة',
    'item-categories': 'فئات الأصناف', items: 'الأصناف',
    promotions: 'العروض', 'payment-links': 'روابط الدفع', reviews: 'التقييمات', reports: 'التقارير', settings: 'الإعدادات'
  },
  newOrdersBadge: (count: number) => `${count} طلبات جديدة`,
  live: 'مباشر',
  offline: 'غير متصل: أعد تحميل الصفحة لإعادة الاتصال',
  connecting: 'جارٍ الاتصال…',
  signOut: 'تسجيل الخروج',
  dismiss: 'إخفاء',
  roles: { Owner: 'المالك', Manager: 'المدير', Staff: 'موظف', Admin: 'مسؤول النظام' },
  locationTypes: { Table: 'طاولة', Counter: 'كاونتر', HotelRoom: 'غرفة', PoolBeach: 'المسبح/الشاطئ' },
  noArea: 'بدون منطقة',
  newOrder: (number: number | string) => `طلب جديد #${number}`,
  paymentReceived: 'تم استلام دفعة',
  billPaid: 'تم دفع الفاتورة بالكامل',
  tip: (amount: string) => `إكرامية ${amount}`,
  sharesOf: (shares: number, people: number) => `${shares} من ${people} حصص`,
  items: (count: number) => (count === 1 ? 'صنف واحد' : `${count} أصناف`),
  customAmount: 'مبلغ مخصص',
  newReview: 'تقييم جديد',
  refundUpdate: 'تحديث على الاسترداد',
  linkPaid: 'تم دفع رابط الدفع',
  view: 'عرض',
  openOrders: 'فتح الطلبات',
  openTransactions: 'فتح المعاملات',
  openPaymentLinks: 'فتح روابط الدفع',
  openReviews: 'فتح التقييمات',
  refundConfirmed: 'تم تأكيد الاسترداد',
  refundDeclined: 'تم رفض الاسترداد',
  ref: (reference: string) => `المرجع ${reference}`,
  paidInFull: 'تم الدفع بالكامل',
  stillToPay: (amount: string) => `متبقٍ ${amount} للدفع`,
  stars: (count: number) => `${count} نجوم`,
  withComment: 'مع تعليق',
  signIn: 'تسجيل الدخول',
  loading: 'جارٍ التحميل…',
  save: 'حفظ',
  saving: 'جارٍ الحفظ…',
  cancel: 'إلغاء',
  close: 'إغلاق',
  edit: 'تعديل',
  delete: 'حذف',
  add: 'إضافة',
  search: 'بحث',
  all: 'الكل',
  yes: 'نعم',
  no: 'لا',
  none: 'لا شيء',
  retry: 'حاول مرة أخرى',
  comingSoon: (sprint: number) => `قريباً في المرحلة ${sprint}.`,
  apiUnreachable: 'تعذّر الوصول إلى خادم DineFlow. هل هو قيد التشغيل؟',
  forbidden: 'صلاحياتك لا تسمح بهذا التغيير. اطلب ذلك من المالك أو المدير.',
  requestFailed: (status: number) => `فشل الطلب (${status}).`,
  somethingWrong: 'حدث خطأ ما.',
  noVenue: 'لا يوجد فرع مرتبط بهذا الحساب بعد.'
};

const COMMON: Lexicon<Common> = { en: EN, ar: AR };
const STORAGE_KEY = 'df.merchant.lang';

/** The language last chosen in this browser, used before Angular starts (and by plain helpers). */
let current: Lang = readLang();

/** Merchant-portal language: English or Arabic, with the page direction following it. */
@Injectable({ providedIn: 'root' })
export class I18n {
  private readonly document = inject(DOCUMENT);

  readonly lang = signal<Lang>(current);
  /** Strings shared by every page. */
  readonly t = this.use(COMMON);
  /** BCP 47 locale for dates and numbers; Arabic keeps Latin digits, as staff and POS slips use them. */
  readonly locale = computed(() => localeOf(this.lang()));

  constructor() {
    effect(() => {
      const lang = this.lang();
      current = lang;
      this.document.documentElement.lang = lang;
      this.document.documentElement.dir = lang === 'ar' ? 'rtl' : 'ltr';
    });
  }

  /** A page's strings in the current language. */
  use<T>(lexicon: Lexicon<T>): Signal<T> {
    return computed(() => lexicon[this.lang()]);
  }

  toggle(): void {
    this.lang.set(this.lang() === 'en' ? 'ar' : 'en');
    try {
      localStorage.setItem(STORAGE_KEY, this.lang());
    } catch {
      // Storage unavailable: the choice lasts until the page is reloaded.
    }
  }
}

/** The current language's locale, for formatting helpers outside components. */
export function currentLocale(): string {
  return localeOf(current);
}

/** The shared strings in the current language, for helpers outside components (e.g. errorMessage). */
export function commonStrings(): Common {
  return COMMON[current];
}

/** Asks the API to answer (error messages included) in the portal's language. */
export const languageInterceptor: HttpInterceptorFn = (req, next) =>
  next(req.url.startsWith('/v1/') ? req.clone({ setHeaders: { 'Accept-Language': current } }) : req);

function localeOf(lang: Lang): string {
  return lang === 'ar' ? 'ar-JO-u-nu-latn' : 'en-GB';
}

function readLang(): Lang {
  try {
    return localStorage.getItem(STORAGE_KEY) === 'ar' ? 'ar' : 'en';
  } catch {
    return 'en';
  }
}
