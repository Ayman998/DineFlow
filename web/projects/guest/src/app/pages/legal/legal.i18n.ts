import { Lang } from '../../core/i18n';

/**
 * The guest-facing policies: terms, privacy, refunds and contact, in English and Arabic.
 *
 * They describe how helloZen actually works (Mastercard Gateway hosted payments, card brand and
 * last four digits only, receipts by e-mail, feedback contact details by consent, 14 days of server
 * logs, hosting in the EU), so change them with the product. They were drafted for the Amman pilot
 * and should be reviewed by a Jordanian lawyer. When the terms change, update `updated` here and
 * bump Payments:TermsVersion on the API, which is recorded with every payment.
 */

export type LegalDoc = 'terms' | 'privacy' | 'refunds' | 'contact';

/** The business behind helloZen, from the API's Legal settings; missing values are left out. */
export interface Operator {
  legalName: string | null;
  address: string | null;
  email: string | null;
  phone: string | null;
  registrationNumber: string | null;
}

export interface Section {
  heading: string;
  paragraphs: string[];
}

interface Lexicon {
  updated: string;
  back: string;
  nav: Record<LegalDoc, string>;
  titles: Record<LegalDoc, string>;
  intros: Record<LegalDoc, string>;
  /** " helloZen is operated by <legal name>." once the name is set, otherwise nothing. */
  operatedBy: (o: Operator) => string;
  contactUs: (o: Operator) => string;
  terms: (o: Operator) => Section[];
  privacy: (o: Operator) => Section[];
  refunds: (o: Operator) => Section[];
  contact: {
    restaurantHeading: string;
    restaurantBody: string;
    usHeading: string;
    usBody: string;
    email: string;
    phone: string;
    address: string;
    company: string;
    registration: string;
    reference: string;
    notSet: string;
  };
}

const UPDATED_ON = '2026-11-03';

const EN: Lexicon = {
  updated: 'Last updated 3 November 2026',
  back: 'Back',
  nav: { terms: 'Terms', privacy: 'Privacy', refunds: 'Refunds', contact: 'Contact' },
  titles: { terms: 'Terms and Conditions', privacy: 'Privacy Policy', refunds: 'Refunds and Cancellations', contact: 'Contact us' },
  intros: {
    terms: 'These terms apply when you use helloZen to view a menu, order or pay at a restaurant. By paying, you accept them.',
    privacy: 'What information helloZen uses when you order and pay at a restaurant, why, who sees it, and your choices.',
    refunds: 'How to change or cancel an order, and how refunds work when something goes wrong.',
    contact: 'Who to ask, and how to reach us.'
  },
  operatedBy: o => (o.legalName ? ` helloZen is operated by ${o.legalName}.` : ''),
  contactUs: o => (o.email ? `write to us at ${o.email}` : 'use the details on our Contact page'),
  terms: o => [
    {
      heading: '1. Who we are',
      paragraphs: [
        `helloZen ("we") lets you open a restaurant's menu, order and pay from your phone by scanning the QR code on your table, or from a payment link the restaurant sends you. We provide this service on behalf of the restaurant you visit ("the restaurant").${EN.operatedBy(o)}`
      ]
    },
    {
      heading: "2. The restaurant's part",
      paragraphs: [
        'The restaurant sets its menu, prices, taxes and service charge, prepares your order and is responsible for the food, drinks and service. Prices are shown in the restaurant\'s currency (Jordanian dinars in Jordan) and include the taxes and charges shown on your bill.'
      ]
    },
    {
      heading: '3. Ordering',
      paragraphs: [
        "An order you send is a request to the restaurant. It's confirmed when staff accept it, and they may decline it, for example when an item has run out. To change or cancel an order, ask a member of staff."
      ]
    },
    {
      heading: '4. Paying',
      paragraphs: [
        "Card and wallet payments are processed by the restaurant's bank through Mastercard Gateway, on the bank's secure payment page. helloZen never sees or stores your full card number or security code.",
        "A payment is complete only when the bank confirms it. If it's declined or you go back without paying, you aren't charged. When a bill is split, you pay only the share you choose, and the bill is settled once all of it is paid.",
        'Tips are optional and go to the restaurant. You can have a receipt e-mailed to you after paying.'
      ]
    },
    {
      heading: '5. Refunds',
      paragraphs: ['Refunds are decided by the restaurant and returned to the card you paid with. Our Refunds and Cancellations page explains how.']
    },
    {
      heading: '6. Reviews and feedback',
      paragraphs: [
        'If you rate your visit, the restaurant sees your rating and comments. Your name, phone number or e-mail address are shared with the restaurant only if you choose to give them with your feedback.'
      ]
    },
    {
      heading: '7. Using helloZen fairly',
      paragraphs: [
        "Please don't place orders you don't intend to pay for, use another table's QR code or payment link without permission, or try to interfere with the service or other guests' bills."
      ]
    },
    {
      heading: '8. Our responsibility',
      paragraphs: [
        "We take reasonable care to keep helloZen working and accurate, but we aren't responsible for the food, drinks or service the restaurant provides, or for delays caused by your connection or your bank. Nothing in these terms limits rights you have under Jordanian consumer protection law."
      ]
    },
    {
      heading: '9. Changes and law',
      paragraphs: [
        'We may update these terms. The version you accepted is recorded with your payment. These terms are governed by the laws of the Hashemite Kingdom of Jordan, and the courts of Amman have jurisdiction.',
        `Questions? Ask the restaurant's staff, or ${EN.contactUs(o)}.`
      ]
    }
  ],
  privacy: o => [
    {
      heading: '1. Who is responsible',
      paragraphs: [
        `helloZen runs the ordering and payment service for the restaurant you visit.${EN.operatedBy(o)} We and the restaurant use your information only as described here, and in line with Jordan's Personal Data Protection Law.`
      ]
    },
    {
      heading: '2. What we collect',
      paragraphs: [
        'The table or link you used and when; what you order; the amount, tip and result of each payment, with the card type and last four digits the bank reports (never the full card number or security code).',
        'Your e-mail address, if you ask for a receipt. Your rating and comments, and any name, phone number or e-mail address you choose to share with feedback.',
        'Technical details such as your IP address and browser, which our servers record for security.'
      ]
    },
    {
      heading: '3. How we use it',
      paragraphs: [
        'To send your order to the restaurant, take your payment, split the bill, send your receipt, let the restaurant reply to feedback when you asked it to, keep accounts and prevent fraud. We don\'t sell your information, show you advertising, or send you marketing messages.'
      ]
    },
    {
      heading: '4. Who sees it',
      paragraphs: [
        "The restaurant you visited; the restaurant's bank and Mastercard Gateway, to process payments and refunds; and the companies that host helloZen and deliver its e-mail, whose servers are in the European Union. We share information with authorities only when the law requires it."
      ]
    },
    {
      heading: '5. On your phone',
      paragraphs: [
        "helloZen keeps your table session, cart and language choice in your browser's storage on your phone so the page works when you return to it. It doesn't use advertising or tracking cookies."
      ]
    },
    {
      heading: '6. How long we keep it',
      paragraphs: [
        'Order and payment records are kept for as long as Jordanian accounting and tax law requires. Server logs are deleted after 14 days. Contact details you shared with feedback are kept until you ask us to delete them.'
      ]
    },
    {
      heading: '7. Your choices',
      paragraphs: [
        `You can ask to see, correct or delete your information, or object to how it's used. To do so, ${EN.contactUs(o)}. Records the law requires us to keep, such as payments, can't be deleted before that time.`
      ]
    },
    {
      heading: '8. Security',
      paragraphs: [
        'helloZen is only reachable over an encrypted connection (HTTPS). Payments are entered on the bank\'s secure page. Only the restaurant\'s staff with their own sign-in can see its orders and payments.'
      ]
    }
  ],
  refunds: o => [
    {
      heading: '1. Before you pay',
      paragraphs: ["To change or cancel an order, ask a member of staff. Orders the kitchen hasn't accepted yet can usually be cancelled; once food is being prepared, it's up to the restaurant."]
    },
    {
      heading: '2. Something wrong with your order',
      paragraphs: ["Tell the staff while you're at the restaurant: it's the quickest way to have it put right, or taken off your bill before you pay."]
    },
    {
      heading: '3. Refunds',
      paragraphs: [
        'The restaurant decides on refunds, in full or in part. A refund always goes back to the card or wallet you paid with; helloZen can\'t send it to another card or pay it in cash.',
        'Once approved, the refund is sent straight away. It usually reaches your account within 5 to 14 business days, depending on your bank.'
      ]
    },
    {
      heading: '4. Charged twice, or more than you owed',
      paragraphs: [
        `If a payment went through twice, or a shared bill was overpaid, ask the restaurant or ${EN.contactUs(o)}. Give the payment reference from your receipt (it starts with DF-), and the extra is refunded.`
      ]
    },
    {
      heading: '5. Payments that didn\'t go through',
      paragraphs: ["If a payment was declined or you cancelled it, you aren't charged. If your bank shows a pending amount, it releases it by itself, usually within a few days."]
    }
  ],
  contact: {
    restaurantHeading: 'About your order or bill',
    restaurantBody: 'Ask the restaurant\'s staff: they can change an order, fix a bill or start a refund fastest.',
    usHeading: 'About helloZen, a refund after your visit, or your data',
    usBody: 'Include the payment reference from your receipt (it starts with DF-) if your question is about a payment.',
    email: 'E-mail',
    phone: 'Phone',
    address: 'Address',
    company: 'Company',
    registration: 'Registration no.',
    reference: 'Payment reference',
    notSet: 'Contact details will be published here shortly. Meanwhile, please ask the restaurant\'s staff.'
  }
};

const AR: Lexicon = {
  updated: 'آخر تحديث: 3 تشرين الثاني 2026',
  back: 'رجوع',
  nav: { terms: 'الشروط', privacy: 'الخصوصية', refunds: 'الاسترداد', contact: 'اتصل بنا' },
  titles: { terms: 'الشروط والأحكام', privacy: 'سياسة الخصوصية', refunds: 'الاسترداد والإلغاء', contact: 'اتصل بنا' },
  intros: {
    terms: 'تنطبق هذه الشروط عند استخدامك helloZen لتصفح قائمة مطعم أو الطلب أو الدفع. بإتمامك الدفع فإنك توافق عليها.',
    privacy: 'ما المعلومات التي يستخدمها helloZen عند طلبك ودفعك في مطعم، ولماذا، ومن يطّلع عليها، وما خياراتك.',
    refunds: 'كيف تعدّل طلبك أو تلغيه، وكيف يتم الاسترداد إذا حدث خطأ ما.',
    contact: 'بمن تتصل، وكيف تصل إلينا.'
  },
  operatedBy: o => (o.legalName ? ` وتشغّل ${o.legalName} خدمة helloZen.` : ''),
  contactUs: o => (o.email ? `راسلنا على ${o.email}` : 'استخدم بيانات التواصل في صفحة «اتصل بنا»'),
  terms: o => [
    {
      heading: '1. من نحن',
      paragraphs: [
        `يتيح لك helloZen («نحن») فتح قائمة المطعم والطلب والدفع من هاتفك بمسح رمز QR الموجود على طاولتك، أو من رابط دفع يرسله إليك المطعم. نقدّم هذه الخدمة نيابةً عن المطعم الذي تزوره («المطعم»).${AR.operatedBy(o)}`
      ]
    },
    {
      heading: '2. دور المطعم',
      paragraphs: [
        'يحدد المطعم قائمته وأسعاره والضرائب ورسوم الخدمة، ويحضّر طلبك، وهو المسؤول عن الطعام والمشروبات والخدمة. تُعرض الأسعار بعملة المطعم (الدينار الأردني في الأردن) وتشمل الضرائب والرسوم الظاهرة على فاتورتك.'
      ]
    },
    {
      heading: '3. الطلب',
      paragraphs: ['الطلب الذي ترسله هو طلب مقدَّم إلى المطعم، ويُؤكَّد عند قبول الموظفين له، ويحق لهم رفضه، كأن ينفد أحد الأصناف. لتعديل طلبك أو إلغائه، اطلب ذلك من أحد الموظفين.']
    },
    {
      heading: '4. الدفع',
      paragraphs: [
        'تتم معالجة المدفوعات بالبطاقات والمحافظ عبر بنك المطعم من خلال Mastercard Gateway، على صفحة الدفع الآمنة الخاصة بالبنك. لا يطّلع helloZen على رقم بطاقتك الكامل أو رمز الأمان ولا يخزّنهما أبداً.',
        'لا تكتمل عملية الدفع إلا بعد تأكيد البنك لها. إذا رُفضت العملية أو عدت دون أن تدفع، فلن يُخصم منك أي مبلغ. عند تقسيم الفاتورة تدفع الحصة التي تختارها فقط، وتُسدَّد الفاتورة عند دفعها بالكامل.',
        'الإكرامية اختيارية وتذهب إلى المطعم. ويمكنك طلب إرسال الإيصال إلى بريدك الإلكتروني بعد الدفع.'
      ]
    },
    {
      heading: '5. الاسترداد',
      paragraphs: ['يقرر المطعم الاسترداد، ويُعاد المبلغ إلى البطاقة التي دفعت بها. توضّح صفحة «الاسترداد والإلغاء» الطريقة.']
    },
    {
      heading: '6. التقييمات والملاحظات',
      paragraphs: ['إذا قيّمت زيارتك، يرى المطعم تقييمك وتعليقاتك. ولا تتم مشاركة اسمك أو رقم هاتفك أو بريدك الإلكتروني مع المطعم إلا إذا اخترت تقديمها مع ملاحظاتك.']
    },
    {
      heading: '7. الاستخدام العادل',
      paragraphs: ['يُرجى عدم إرسال طلبات لا تنوي دفع ثمنها، أو استخدام رمز QR أو رابط دفع لطاولة أخرى دون إذن، أو محاولة التأثير على الخدمة أو على فواتير الضيوف الآخرين.']
    },
    {
      heading: '8. مسؤوليتنا',
      paragraphs: [
        'نبذل عناية معقولة ليعمل helloZen بشكل صحيح ودقيق، لكننا لسنا مسؤولين عن الطعام أو المشروبات أو الخدمة التي يقدّمها المطعم، ولا عن التأخير الناتج عن اتصالك أو عن بنكك. لا يحدّ أي شيء في هذه الشروط من حقوقك بموجب قانون حماية المستهلك الأردني.'
      ]
    },
    {
      heading: '9. التعديلات والقانون الواجب التطبيق',
      paragraphs: [
        'قد نحدّث هذه الشروط، ويُسجَّل الإصدار الذي وافقت عليه مع دفعتك. تخضع هذه الشروط لقوانين المملكة الأردنية الهاشمية، وتختص محاكم عمّان بالنظر في أي نزاع.',
        `لديك سؤال؟ اسأل موظفي المطعم، أو ${AR.contactUs(o)}.`
      ]
    }
  ],
  privacy: o => [
    {
      heading: '1. الجهة المسؤولة',
      paragraphs: [
        `يقدّم helloZen خدمة الطلب والدفع لصالح المطعم الذي تزوره.${AR.operatedBy(o)} نستخدم نحن والمطعم معلوماتك فقط كما هو موضّح هنا، وبما يتوافق مع قانون حماية البيانات الشخصية الأردني.`
      ]
    },
    {
      heading: '2. ما نجمعه',
      paragraphs: [
        'الطاولة أو الرابط الذي استخدمته ووقت ذلك؛ وما تطلبه؛ ومبلغ كل دفعة والإكرامية ونتيجتها، مع نوع البطاقة وآخر أربعة أرقام منها كما يبلغنا بها البنك (وليس رقم البطاقة الكامل أو رمز الأمان أبداً).',
        'بريدك الإلكتروني إذا طلبت إيصالاً. وتقييمك وتعليقاتك، وأي اسم أو رقم هاتف أو بريد إلكتروني تختار مشاركته مع ملاحظاتك.',
        'بيانات تقنية مثل عنوان IP ونوع المتصفح، تسجّلها خوادمنا لأغراض الأمان.'
      ]
    },
    {
      heading: '3. كيف نستخدمها',
      paragraphs: [
        'لإرسال طلبك إلى المطعم، وتحصيل دفعتك، وتقسيم الفاتورة، وإرسال إيصالك، وتمكين المطعم من الرد على ملاحظاتك عندما تطلب ذلك، ولمسك الحسابات ومنع الاحتيال. لا نبيع معلوماتك، ولا نعرض عليك إعلانات، ولا نرسل إليك رسائل تسويقية.'
      ]
    },
    {
      heading: '4. من يطّلع عليها',
      paragraphs: [
        'المطعم الذي زرته؛ وبنك المطعم وMastercard Gateway لمعالجة المدفوعات والاستردادات؛ والشركات التي تستضيف helloZen وترسل بريده الإلكتروني، وتقع خوادمها في الاتحاد الأوروبي. لا نشارك المعلومات مع الجهات الرسمية إلا عندما يقتضي القانون ذلك.'
      ]
    },
    {
      heading: '5. على هاتفك',
      paragraphs: ['يحفظ helloZen جلسة طاولتك وسلة طلبك ولغتك المختارة في ذاكرة المتصفح على هاتفك، لتعمل الصفحة عند عودتك إليها. ولا يستخدم ملفات تعريف ارتباط إعلانية أو للتتبّع.']
    },
    {
      heading: '6. مدة الاحتفاظ',
      paragraphs: [
        'نحتفظ بسجلات الطلبات والمدفوعات طوال المدة التي تفرضها قوانين المحاسبة والضرائب الأردنية. وتُحذف سجلات الخوادم بعد 14 يوماً. ونحتفظ ببيانات التواصل التي شاركتها مع ملاحظاتك إلى أن تطلب منا حذفها.'
      ]
    },
    {
      heading: '7. خياراتك',
      paragraphs: [
        `يحق لك طلب الاطلاع على معلوماتك أو تصحيحها أو حذفها، أو الاعتراض على طريقة استخدامها. لذلك، ${AR.contactUs(o)}. ولا يمكن حذف السجلات التي يلزمنا القانون بالاحتفاظ بها، كالمدفوعات، قبل انقضاء مدتها.`
      ]
    },
    {
      heading: '8. الأمان',
      paragraphs: ['لا يمكن الوصول إلى helloZen إلا عبر اتصال مشفّر (HTTPS)، وتُدخَل بيانات الدفع على صفحة البنك الآمنة. ولا يرى طلبات المطعم ومدفوعاته إلا موظفوه الذين لديهم حسابات دخول خاصة بهم.']
    }
  ],
  refunds: o => [
    {
      heading: '1. قبل الدفع',
      paragraphs: ['لتعديل طلبك أو إلغائه، اطلب ذلك من أحد الموظفين. يمكن عادةً إلغاء الطلبات التي لم يقبلها المطبخ بعد؛ أما بعد البدء بتحضير الطعام فالقرار للمطعم.']
    },
    {
      heading: '2. مشكلة في طلبك',
      paragraphs: ['أخبر الموظفين وأنت في المطعم، فهي أسرع طريقة لتصحيح الأمر أو إزالته من فاتورتك قبل الدفع.']
    },
    {
      heading: '3. الاسترداد',
      paragraphs: [
        'يقرر المطعم الاسترداد كلياً أو جزئياً. ويُعاد المبلغ دائماً إلى البطاقة أو المحفظة التي دفعت بها، ولا يستطيع helloZen إرساله إلى بطاقة أخرى أو دفعه نقداً.',
        'بعد الموافقة يُرسَل المبلغ فوراً، ويصل إلى حسابك عادةً خلال 5 إلى 14 يوم عمل حسب بنكك.'
      ]
    },
    {
      heading: '4. خصم مرتين أو أكثر من المستحق',
      paragraphs: [
        `إذا تمت دفعة مرتين، أو دُفع أكثر من قيمة فاتورة مشتركة، فاطلب ذلك من المطعم أو ${AR.contactUs(o)}، مع ذكر رقم مرجع الدفعة من إيصالك (يبدأ بـ DF-)، وسيُعاد المبلغ الزائد.`
      ]
    },
    {
      heading: '5. دفعات لم تكتمل',
      paragraphs: ['إذا رُفضت الدفعة أو ألغيتها، فلن يُخصم منك شيء. وإذا ظهر لدى بنكك مبلغ معلّق، فإنه يُفرج عنه تلقائياً خلال أيام قليلة عادةً.']
    }
  ],
  contact: {
    restaurantHeading: 'بخصوص طلبك أو فاتورتك',
    restaurantBody: 'اسأل موظفي المطعم، فهم الأسرع في تعديل الطلب أو تصحيح الفاتورة أو بدء الاسترداد.',
    usHeading: 'بخصوص helloZen، أو استرداد بعد زيارتك، أو بياناتك',
    usBody: 'إذا كان سؤالك عن دفعة، فاذكر رقم مرجعها من إيصالك (يبدأ بـ DF-).',
    email: 'البريد الإلكتروني',
    phone: 'الهاتف',
    address: 'العنوان',
    company: 'الشركة',
    registration: 'رقم التسجيل',
    reference: 'مرجع الدفعة',
    notSet: 'ستُنشر بيانات التواصل هنا قريباً. وإلى ذلك الحين، يُرجى سؤال موظفي المطعم.'
  }
};

export const LEGAL: Record<Lang, Lexicon> = { en: EN, ar: AR };

/** When the texts were last changed, for <time datetime>. */
export const LEGAL_UPDATED_ON = UPDATED_ON;
