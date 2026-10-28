import { Lexicon } from '../core/i18n';

const en = {
  deliveryLog: 'Delivery log',
  failedCount: (count: number) => `${count} failed`,
  nothingSent: 'Nothing sent yet.',
  attempts: (count: number) => `${count} attempts`,
  nextTry: (when: string) => `next try ${when}`,
  resend: 'Resend',
  showing: (shown: number, total: number) => `Showing the latest ${shown} of ${total}.`,
  kinds: { PaymentAlert: 'Payment alert', LinkPaymentAlert: 'Payment link alert', Report: 'Report', Test: 'Test' } as Record<string, string>,
  statuses: { Sent: 'Sent', Failed: 'Failed', Queued: 'Queued', Retrying: 'Retrying' } as Record<string, string>
};

export const DELIVERY_LOG: Lexicon<typeof en> = {
  en,
  ar: {
    deliveryLog: 'سجل الإرسال',
    failedCount: (count: number) => `فشل ${count}`,
    nothingSent: 'لم يُرسَل شيء بعد.',
    // Only shown for two or more attempts.
    attempts: (count: number) => (count === 2 ? 'محاولتان' : count % 100 >= 3 && count % 100 <= 10 ? `${count} محاولات` : `${count} محاولة`),
    nextTry: (when: string) => `المحاولة التالية ${when}`,
    resend: 'إعادة الإرسال',
    showing: (shown: number, total: number) => `عرض أحدث ${shown} من أصل ${total}.`,
    kinds: { PaymentAlert: 'تنبيه دفعة', LinkPaymentAlert: 'تنبيه رابط دفع', Report: 'تقرير', Test: 'اختبار' },
    statuses: { Sent: 'تم الإرسال', Failed: 'فشل', Queued: 'قيد الانتظار', Retrying: 'جارٍ إعادة المحاولة' }
  }
};
