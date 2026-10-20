import { DOCUMENT, Injectable, computed, effect, inject, signal } from '@angular/core';

import { readJson, writeJson } from './storage';

export type Lang = 'en' | 'ar';

const EN = {
  languageName: 'English',
  otherLanguage: 'العربية',
  changeLanguage: 'Change language',
  homeTitle: 'Scan, order and pay at your table',
  homeBody: 'Scan the QR code on your table to open the menu.',
  api: 'API',
  online: 'online',
  offline: 'offline',
  welcome: 'Welcome',
  whatToDo: 'What would you like to do?',
  viewMenu: 'View Menu',
  payBill: 'Pay Bill',
  noAppNeeded: 'No app or account needed',
  poweredBy: 'Powered by DineFlow',
  terms: 'Terms',
  loading: 'Opening your table…',
  invalidTitle: "This QR code isn't active",
  invalidBody: 'It may have been replaced. Please ask a member of staff for help.',
  unavailableTitle: "This venue isn't available right now",
  unavailableBody: "Ordering and payments are paused. Please ask a member of staff for help.",
  errorTitle: "We couldn't open your table",
  errorBody: 'Check your connection and try again.',
  tryAgain: 'Try again',
  back: 'Back',
  menuSoonTitle: 'The menu is on its way',
  menuSoonBody: 'Browsing and ordering from the menu arrives in the next release.',
  billSoonTitle: 'Bill payment is on its way',
  billSoonBody: 'Paying your bill from your phone arrives in a later release.',
  locationTypes: { Table: 'Table', Counter: 'Counter', HotelRoom: 'Room', PoolBeach: 'Pool/Beach' } as Record<string, string>,
  payBillShort: 'Pay bill',
  chooseMenu: 'Choose menu',
  allDay: 'All day',
  until: (time: string) => `until ${time}`,
  servedBetween: (from: string, to: string) => `served ${from}–${to}`,
  notServingNow: 'not available now',
  menuClosedNotice: (name: string, when: string) => `${name} is ${when}. You can browse, but ordering opens then.`,
  menuClosedToday: (name: string) => `${name} isn't available right now. You can browse, but not order from it.`,
  gridView: 'Grid view',
  listView: 'List view',
  searchMenu: 'Search the menu',
  all: 'All',
  noResults: 'Nothing matches your search.',
  emptyMenu: 'This menu has no items yet.',
  unavailableNow: 'Unavailable right now',
  addOne: (name: string) => `Add one ${name}`,
  removeOne: (name: string) => `Remove one ${name}`,
  customize: (name: string) => `Choose options for ${name}`,
  inOrder: (count: number) => `${count} in your order`,
  contains: (allergen: string) => `Contains ${allergen.toLowerCase()}`,
  allergenNames: {
    Gluten: 'Gluten', Crustaceans: 'Crustaceans', Eggs: 'Eggs', Fish: 'Fish', Peanuts: 'Peanuts', Soy: 'Soy',
    Milk: 'Dairy', TreeNuts: 'Tree nuts', Celery: 'Celery', Mustard: 'Mustard', Sesame: 'Sesame',
    Sulphites: 'Sulphites', Lupin: 'Lupin', Molluscs: 'Molluscs'
  } as Record<string, string>,
  close: 'Close',
  required: 'Required',
  optional: 'Optional',
  pick: (min: number, max: number) => (min === max ? `pick ${max}` : min > 0 ? `pick ${min}–${max}` : `up to ${max}`),
  included: 'included',
  soldOut: 'Sold out',
  specialRequests: 'Allergies or special requests',
  specialRequestsPlaceholder: 'e.g. no olives, nut allergy',
  decreaseQuantity: 'Decrease quantity',
  increaseQuantity: 'Increase quantity',
  addToOrder: (price: string) => `Add to order · ${price}`,
  chooseRequired: (group: string) => `Choose ${group.toLowerCase()}`,
  added: (name: string) => `Added ${name} to your order`,
  viewOrder: 'View order',
  yourOrder: 'Your order',
  orderEmpty: 'Your order is empty.',
  backToMenu: 'Back to menu',
  remove: 'Remove',
  total: 'Total',
  orderingSoon: 'Placing the order from your phone arrives in the next release. Show this list to a member of staff for now.',
  sessionEndedTitle: 'Your table session has ended',
  sessionEndedBody: 'Scan the QR code on your table again to keep browsing.',
  menuOffTitle: "This venue doesn't have a digital menu",
  menuOffBody: 'Please ask a member of staff for a menu.'
};

type Strings = typeof EN;

const AR: Strings = {
  languageName: 'العربية',
  otherLanguage: 'English',
  changeLanguage: 'تغيير اللغة',
  homeTitle: 'امسح، اطلب وادفع من طاولتك',
  homeBody: 'امسح رمز QR الموجود على طاولتك لفتح القائمة.',
  api: 'الخادم',
  online: 'متصل',
  offline: 'غير متصل',
  welcome: 'أهلاً بك',
  whatToDo: 'ماذا تود أن تفعل؟',
  viewMenu: 'عرض القائمة',
  payBill: 'دفع الفاتورة',
  noAppNeeded: 'لا حاجة لتطبيق أو حساب',
  poweredBy: 'مدعوم من DineFlow',
  terms: 'الشروط',
  loading: 'جارٍ فتح طاولتك…',
  invalidTitle: 'رمز QR هذا غير مفعّل',
  invalidBody: 'ربما تم استبداله. يرجى طلب المساعدة من أحد الموظفين.',
  unavailableTitle: 'هذا المكان غير متاح حالياً',
  unavailableBody: 'الطلبات والمدفوعات متوقفة مؤقتاً. يرجى طلب المساعدة من أحد الموظفين.',
  errorTitle: 'تعذّر فتح طاولتك',
  errorBody: 'تحقق من اتصالك وحاول مرة أخرى.',
  tryAgain: 'حاول مرة أخرى',
  back: 'رجوع',
  menuSoonTitle: 'القائمة قادمة قريباً',
  menuSoonBody: 'تصفح القائمة والطلب منها متاح في الإصدار القادم.',
  billSoonTitle: 'دفع الفاتورة قادم قريباً',
  billSoonBody: 'دفع فاتورتك من هاتفك متاح في إصدار لاحق.',
  locationTypes: { Table: 'طاولة', Counter: 'كاونتر', HotelRoom: 'غرفة', PoolBeach: 'المسبح/الشاطئ' },
  payBillShort: 'دفع الفاتورة',
  chooseMenu: 'اختر القائمة',
  allDay: 'طوال اليوم',
  until: (time: string) => `حتى ${time}`,
  servedBetween: (from: string, to: string) => `يُقدَّم ${from}–${to}`,
  notServingNow: 'غير متاحة الآن',
  menuClosedNotice: (name: string, when: string) => `قائمة ${name} ${when}. يمكنك التصفح، ويبدأ الطلب في ذلك الوقت.`,
  menuClosedToday: (name: string) => `قائمة ${name} غير متاحة الآن. يمكنك التصفح دون الطلب منها.`,
  gridView: 'عرض شبكي',
  listView: 'عرض قائمة',
  searchMenu: 'ابحث في القائمة',
  all: 'الكل',
  noResults: 'لا توجد نتائج مطابقة.',
  emptyMenu: 'لا توجد أصناف في هذه القائمة بعد.',
  unavailableNow: 'غير متوفر حالياً',
  addOne: (name: string) => `أضف ${name}`,
  removeOne: (name: string) => `احذف واحداً من ${name}`,
  customize: (name: string) => `اختر إضافات ${name}`,
  inOrder: (count: number) => `${count} في طلبك`,
  contains: (allergen: string) => `يحتوي على ${allergen}`,
  allergenNames: {
    Gluten: 'الغلوتين', Crustaceans: 'القشريات', Eggs: 'البيض', Fish: 'السمك', Peanuts: 'الفول السوداني', Soy: 'الصويا',
    Milk: 'الألبان', TreeNuts: 'المكسرات', Celery: 'الكرفس', Mustard: 'الخردل', Sesame: 'السمسم',
    Sulphites: 'الكبريتيت', Lupin: 'الترمس', Molluscs: 'الرخويات'
  },
  close: 'إغلاق',
  required: 'إجباري',
  optional: 'اختياري',
  pick: (min: number, max: number) => (min === max ? `اختر ${max}` : min > 0 ? `اختر ${min}–${max}` : `حتى ${max}`),
  included: 'مشمول',
  soldOut: 'نفد',
  specialRequests: 'الحساسية أو الطلبات الخاصة',
  specialRequestsPlaceholder: 'مثال: بدون زيتون، حساسية من المكسرات',
  decreaseQuantity: 'تقليل الكمية',
  increaseQuantity: 'زيادة الكمية',
  addToOrder: (price: string) => `أضف إلى الطلب · ${price}`,
  chooseRequired: (group: string) => `اختر ${group}`,
  added: (name: string) => `تمت إضافة ${name} إلى طلبك`,
  viewOrder: 'عرض الطلب',
  yourOrder: 'طلبك',
  orderEmpty: 'طلبك فارغ.',
  backToMenu: 'العودة إلى القائمة',
  remove: 'حذف',
  total: 'المجموع',
  orderingSoon: 'إرسال الطلب من هاتفك متاح في الإصدار القادم. اعرض هذه القائمة على أحد الموظفين حالياً.',
  sessionEndedTitle: 'انتهت جلسة طاولتك',
  sessionEndedBody: 'امسح رمز QR على طاولتك مرة أخرى لمتابعة التصفح.',
  menuOffTitle: 'لا توجد قائمة رقمية في هذا المكان',
  menuOffBody: 'يرجى طلب القائمة من أحد الموظفين.'
};

const STRINGS: Record<Lang, Strings> = { en: EN, ar: AR };
const STORAGE_KEY = 'df.lang';

/** Guest-app language: English or Arabic, with the page direction following it. */
@Injectable({ providedIn: 'root' })
export class I18n {
  private readonly document = inject(DOCUMENT);
  private readonly stored = readJson<Lang>('local', STORAGE_KEY);

  readonly lang = signal<Lang>(this.stored ?? 'en');
  /** True once the guest has picked a language themselves; then the venue default no longer applies. */
  readonly hasPreference = signal(this.stored !== null);
  readonly t = computed(() => STRINGS[this.lang()]);

  constructor() {
    effect(() => {
      const lang = this.lang();
      this.document.documentElement.lang = lang;
      this.document.documentElement.dir = lang === 'ar' ? 'rtl' : 'ltr';
    });
  }

  toggle(): void {
    this.lang.set(this.lang() === 'en' ? 'ar' : 'en');
    this.hasPreference.set(true);
    writeJson('local', STORAGE_KEY, this.lang());
  }

  /** The Arabic text when the guest reads Arabic and one exists, otherwise the English. */
  localize(en: string, ar: string | null | undefined): string;
  localize(en: string | null, ar: string | null | undefined): string | null;
  localize(en: string | null, ar: string | null | undefined): string | null {
    return this.lang() === 'ar' && ar ? ar : en;
  }

  /** Applies the venue's default language unless the guest already chose one. */
  useDefault(lang: string): void {
    if (!this.hasPreference() && (lang === 'en' || lang === 'ar')) this.lang.set(lang);
  }
}
