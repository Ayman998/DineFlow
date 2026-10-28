import { MessageParams, TimelineEvent } from '../core/api';
import { Lexicon, commonStrings } from '../core/i18n';
import { formatMoney } from '../core/money';

/** What a timeline step's wording gets besides its params. */
export interface StepContext {
  /** An amount in minor units, in the payment's currency. */
  money(minor: unknown): string;
  /** "Table 4" in the portal's language. */
  place(type: unknown, label: unknown): string;
}

type Step = (p: MessageParams, c: StepContext) => string;

/**
 * An Arabic count: the singular and dual stand alone ("محاولة واحدة", "محاولتان"); 3–10 take the
 * plural ("3 محاولات"); 0, 11 and up take the singular ("15 محاولة").
 */
function arCount(n: number, one: string, two: string, few: string, many: string): string {
  if (n === 1) return one;
  if (n === 2) return two;
  const rest = n % 100;
  return rest >= 3 && rest <= 10 ? `${n} ${few}` : `${n} ${many}`;
}

const methodsEn: Record<string, string> = { Card: 'Card', ApplePay: 'Apple Pay', GooglePay: 'Google Pay', StcPay: 'STC Pay' };
const methodsAr: Record<string, string> = { Card: 'بطاقة', ApplePay: 'Apple Pay', GooglePay: 'Google Pay', StcPay: 'STC Pay' };
const ledgerEn: Record<string, string> = { bill: 'bill', promotion: 'promotion', tip: 'tip', refund: 'refund' };
const ledgerAr: Record<string, string> = { bill: 'الفاتورة', promotion: 'العرض الترويجي', tip: 'الإكرامية', refund: 'استرداد' };

type LedgerEntry = { type: string; amountMinor: number };

/** Each step the API sends a code for (TransactionService and PaymentLinkService), by code. */
const en = {
  link_created: (p: MessageParams, c: StepContext) =>
    `${p['code'] ? `Payment link ${p['code']}` : 'Link'} created${p['by'] ? ` by ${p['by']}` : ''} for ${c.money(p['amountMinor'])}`,
  link_emailed: (p: MessageParams) => `Link e-mailed to ${p['email']}`,
  session_started: (p: MessageParams, c: StepContext) => `Guest scanned the QR code at ${c.place(p['locationType'], p['label'])}`,
  order_placed: (p: MessageParams, c: StepContext) =>
    `Order #${p['number']} placed · ${c.money(p['amountMinor'])}${p['rejected'] ? ' (later rejected)' : ''}`,
  payment_started: (p: MessageParams, c: StepContext) => {
    const split = p['mode'] === 'Equal' ? ` · ${p['shares']} of ${p['people']} shares`
      : p['mode'] === 'Items' ? ' · by item' : p['mode'] === 'Custom' ? ' · custom amount' : '';
    const method = methodsEn[String(p['method'])] ?? p['method'];
    return `${p['reference'] ? `${p['reference']}: payment started` : 'Payment started'} · ${c.money(p['amountMinor'])} by ${method}${split}`;
  },
  payment_paid: (p: MessageParams, c: StepContext) =>
    `${p['reference']}: paid ${c.money(p['amountMinor'])}${Number(p['tipMinor']) > 0 ? ` (tip ${c.money(p['tipMinor'])})` : ''}`,
  payment_declined: (p: MessageParams) => `${p['reference']}: declined${p['reason'] ? ` (${p['reason']})` : ''}`,
  payment_abandoned: (p: MessageParams) => `${p['reference']}: the customer left the payment page without paying`,
  guest_left: () => 'The guest left the payment page without paying',
  provider_confirmed: (p: MessageParams) => `${p['provider']} confirmed the payment`,
  provider_declined: (p: MessageParams) => `${p['provider']} declined the payment`,
  provider_refund_confirmed: (p: MessageParams) => `${p['provider']} confirmed a refund`,
  provider_refund_declined: (p: MessageParams) => `${p['provider']} declined a refund`,
  provider_reported: (p: MessageParams) => `${p['provider']} reported ${p['status']}`,
  ledger: (p: MessageParams, c: StepContext) =>
    'Ledger: ' + ((p['entries'] ?? []) as LedgerEntry[]).map(e => `${ledgerEn[e.type] ?? e.type} ${c.money(e.amountMinor)}`).join(', '),
  ledger_refund: (p: MessageParams, c: StepContext) => `Ledger: refund ${c.money(p['amountMinor'])}`,
  review: (p: MessageParams) => `Guest rated the visit ${p['rating']} / 5`,
  pos_posted: (p: MessageParams) => `Posted to the POS (${p['provider']}, ${p['reference']})`,
  pos_refused: (p: MessageParams) => `The POS refused the payment: ${p['error']}`,
  pos_waiting: (p: MessageParams) =>
    Number(p['attempts']) ? `Waiting to post to the POS (${p['attempts']} attempts): ${p['error']}` : 'Waiting to post to the POS',
  refund_requested: (p: MessageParams, c: StepContext) =>
    `Refund of ${c.money(p['amountMinor'])} requested${p['by'] ? ` by ${p['by']}` : ''}: ${p['reason']}`,
  refund_failed: (p: MessageParams, c: StepContext) => `Refund of ${c.money(p['amountMinor'])} failed: ${p['reason']}`,
  link_paid: () => 'Paid in full; the link closed',
  link_deactivated: (p: MessageParams) => `Deactivated${p['by'] ? ` by ${p['by']}` : ''}`,
  link_expired: () => 'Expired'
};

export const TIMELINE: Lexicon<typeof en> = {
  en,
  ar: {
    link_created: (p: MessageParams, c: StepContext) =>
      `${p['code'] ? `تم إنشاء رابط الدفع ${p['code']}` : 'تم إنشاء الرابط'}${p['by'] ? ` بواسطة ${p['by']}` : ''} بقيمة ${c.money(p['amountMinor'])}`,
    link_emailed: (p: MessageParams) => `أُرسل الرابط بالبريد الإلكتروني إلى ${p['email']}`,
    session_started: (p: MessageParams, c: StepContext) => `مسح الضيف رمز QR عند ${c.place(p['locationType'], p['label'])}`,
    order_placed: (p: MessageParams, c: StepContext) =>
      `تم تقديم الطلب #${p['number']} · ${c.money(p['amountMinor'])}${p['rejected'] ? ' (رُفض لاحقاً)' : ''}`,
    payment_started: (p: MessageParams, c: StepContext) => {
      const split = p['mode'] === 'Equal' ? ` · ${p['shares']} من ${p['people']} حصص`
        : p['mode'] === 'Items' ? ' · حسب الصنف' : p['mode'] === 'Custom' ? ' · مبلغ مخصص' : '';
      const method = methodsAr[String(p['method'])] ?? p['method'];
      return `${p['reference'] ? `${p['reference']}: ` : ''}بدأت عملية دفع · ${c.money(p['amountMinor'])} عبر ${method}${split}`;
    },
    payment_paid: (p: MessageParams, c: StepContext) =>
      `${p['reference']}: تم دفع ${c.money(p['amountMinor'])}${Number(p['tipMinor']) > 0 ? ` (إكرامية ${c.money(p['tipMinor'])})` : ''}`,
    payment_declined: (p: MessageParams) => `${p['reference']}: رُفضت الدفعة${p['reason'] ? ` (${p['reason']})` : ''}`,
    payment_abandoned: (p: MessageParams) => `${p['reference']}: غادر العميل صفحة الدفع دون أن يدفع`,
    guest_left: () => 'غادر الضيف صفحة الدفع دون أن يدفع',
    provider_confirmed: (p: MessageParams) => `أكّد ${p['provider']} عملية الدفع`,
    provider_declined: (p: MessageParams) => `رفض ${p['provider']} عملية الدفع`,
    provider_refund_confirmed: (p: MessageParams) => `أكّد ${p['provider']} عملية استرداد`,
    provider_refund_declined: (p: MessageParams) => `رفض ${p['provider']} عملية استرداد`,
    provider_reported: (p: MessageParams) => `أبلغ ${p['provider']} عن الحالة ${p['status']}`,
    ledger: (p: MessageParams, c: StepContext) =>
      'دفتر الحركات: ' + ((p['entries'] ?? []) as LedgerEntry[]).map(e => `${ledgerAr[e.type] ?? e.type} ${c.money(e.amountMinor)}`).join('، '),
    ledger_refund: (p: MessageParams, c: StepContext) => `دفتر الحركات: استرداد ${c.money(p['amountMinor'])}`,
    review: (p: MessageParams) => `قيّم الضيف الزيارة ${p['rating']} / 5`,
    pos_posted: (p: MessageParams) => `تم التسجيل في نظام نقاط البيع (${p['provider']}، ${p['reference']})`,
    pos_refused: (p: MessageParams) => `رفض نظام نقاط البيع الدفعة: ${p['error']}`,
    pos_waiting: (p: MessageParams) => {
      const attempts = Number(p['attempts']);
      return attempts
        ? `بانتظار التسجيل في نظام نقاط البيع (${arCount(attempts, 'محاولة واحدة', 'محاولتان', 'محاولات', 'محاولة')}): ${p['error']}`
        : 'بانتظار التسجيل في نظام نقاط البيع';
    },
    refund_requested: (p: MessageParams, c: StepContext) =>
      `طُلب استرداد ${c.money(p['amountMinor'])}${p['by'] ? ` بواسطة ${p['by']}` : ''}: ${p['reason']}`,
    refund_failed: (p: MessageParams, c: StepContext) => `فشل استرداد ${c.money(p['amountMinor'])}: ${p['reason']}`,
    link_paid: () => 'تم الدفع بالكامل وأُغلق الرابط',
    link_deactivated: (p: MessageParams) => `أُلغي تفعيل الرابط${p['by'] ? ` بواسطة ${p['by']}` : ''}`,
    link_expired: () => 'انتهت صلاحية الرابط'
  }
};

/** A timeline step in the portal's language; a step without a known code keeps the API's English text. */
export function timelineText(event: TimelineEvent, steps: typeof en, currency: string): string {
  const step = event.code ? (steps as Record<string, Step | undefined>)[event.code] : undefined;
  if (!step) return event.text;
  return step(event.params ?? {}, {
    money: minor => formatMoney(Number(minor ?? 0), currency),
    place: (type, label) => `${commonStrings().locationTypes[String(type)] ?? type ?? ''} ${label ?? ''}`.trim()
  });
}
