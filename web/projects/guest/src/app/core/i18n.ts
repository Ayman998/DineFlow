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
  locationTypes: { Table: 'Table', Counter: 'Counter', HotelRoom: 'Room', PoolBeach: 'Pool/Beach' } as Record<string, string>
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
  locationTypes: { Table: 'طاولة', Counter: 'كاونتر', HotelRoom: 'غرفة', PoolBeach: 'المسبح/الشاطئ' }
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

  /** Applies the venue's default language unless the guest already chose one. */
  useDefault(lang: string): void {
    if (!this.hasPreference() && (lang === 'en' || lang === 'ar')) this.lang.set(lang);
  }
}
