using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.Headers;
using Microsoft.AspNetCore.Mvc;

namespace DineFlow.Api.Common;

/// <summary>
/// Arabic versions of the API's error messages, for requests whose Accept-Language prefers Arabic.
/// Lookup order: the exact English message (domain validation messages, and codes that have more
/// than one message), then a pattern for interpolated messages, then a generic message for the code.
/// Anything unknown stays in English.
/// </summary>
public static class ArabicErrors
{
    /// <summary>True when the request's highest-ranked supported language is Arabic.</summary>
    public static bool PrefersArabic(HttpRequest request) => RequestedLanguage(request) == "ar";

    /// <summary>
    /// The request's highest-ranked supported language, "en" or "ar", or null when Accept-Language
    /// names neither (or is missing), so the caller can fall back to e.g. the guest's session language.
    /// </summary>
    public static string? RequestedLanguage(HttpRequest request)
    {
        IList<Microsoft.Net.Http.Headers.StringWithQualityHeaderValue> languages;
        try { languages = new RequestHeaders(request.Headers).AcceptLanguage; }
        catch (FormatException) { return null; }

        foreach (var language in languages.OrderByDescending(l => l.Quality ?? 1))
        {
            var tag = language.Value.Value ?? "";
            if (IsLanguage(tag, "ar")) return "ar";
            if (IsLanguage(tag, "en")) return "en";
        }
        return null;

        static bool IsLanguage(string tag, string lang) =>
            tag.Equals(lang, StringComparison.OrdinalIgnoreCase) || tag.StartsWith(lang + "-", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The Arabic for an error, or null when there is none and the English should be kept.</summary>
    public static string? Translate(string code, string english)
    {
        if (ByMessage.TryGetValue(english, out var exact)) return exact;
        foreach (var (pattern, arabic) in Patterns)
        {
            var match = pattern.Match(english);
            if (match.Success) return arabic(match);
        }
        return ByCode.GetValueOrDefault(code);
    }

    public const string ValidationTitle = "بعض البيانات المُدخلة غير صحيحة.";

    /// <summary>
    /// Localizes ASP.NET's automatic model-validation 400s (wired into ProblemDetailsOptions.CustomizeProblemDetails).
    /// Other problem details are left alone: AppExceptionFilter translates those itself.
    /// </summary>
    public static void LocalizeValidation(ProblemDetailsContext context)
    {
        if (context.ProblemDetails is not HttpValidationProblemDetails validation) return;
        if (!PrefersArabic(context.HttpContext.Request)) return;

        validation.Title = ValidationTitle;
        foreach (var key in validation.Errors.Keys.ToList())
            validation.Errors[key] = validation.Errors[key].Select(m => TranslateValidation(m) ?? m).ToArray();
    }

    /// <summary>Arabic for a data-annotation or JSON binding message; the field is named by the errors key.</summary>
    public static string? TranslateValidation(string english)
    {
        if (ValidationMessages.TryGetValue(english, out var exact)) return exact;
        foreach (var (pattern, arabic) in ValidationPatterns)
        {
            var match = pattern.Match(english);
            if (match.Success) return arabic(match);
        }
        return null;
    }

    private static Regex P(string pattern) => new(pattern, RegexOptions.CultureInvariant);

    private static string Field(string english) => FieldNames.GetValueOrDefault(english, english);

    private static readonly Dictionary<string, string> FieldNames = new()
    {
        ["A note"] = "الملاحظة", ["A reason for the guest"] = "السبب الموجّه للضيف", ["A reason"] = "السبب",
        ["Arabic description"] = "الوصف بالعربية", ["Arabic name"] = "الاسم بالعربية", ["Batch reference"] = "مرجع الدفعة",
        ["Branch"] = "الفرع", ["Category name"] = "اسم الفئة", ["Comment"] = "التعليق", ["Dedupe key"] = "مفتاح منع التكرار",
        ["Description"] = "الوصف", ["Email"] = "البريد الإلكتروني", ["Feedback title"] = "عنوان الملاحظات", ["File name"] = "اسم الملف",
        ["Headline"] = "العنوان", ["Image URL"] = "رابط الصورة", ["Item name"] = "اسم الصنف", ["Item note"] = "ملاحظة الصنف",
        ["Menu name"] = "اسم القائمة", ["Modifier group name"] = "اسم مجموعة الإضافات", ["Name"] = "الاسم", ["Option name"] = "اسم الخيار",
        ["Order id"] = "رقم الطلب", ["POS product"] = "منتج نظام نقاط البيع", ["POS table"] = "طاولة نظام نقاط البيع", ["Phone"] = "الهاتف",
        ["Recipient"] = "المستلم", ["Reference"] = "المرجع", ["Refund id"] = "رقم الاسترداد", ["Special request"] = "الطلب الخاص",
        ["Subject"] = "الموضوع", ["Text"] = "النص"
    };

    private static readonly Dictionary<string, string> OrderStatuses = new()
    {
        ["Submitted"] = "مُرسَل", ["Accepted"] = "مقبول", ["Preparing"] = "قيد التحضير", ["Ready"] = "جاهز",
        ["Completed"] = "مكتمل", ["Rejected"] = "مرفوض"
    };

    private static readonly Dictionary<string, string> LocationTypes = new()
    {
        ["Table"] = "الطاولة", ["Counter"] = "الكاونتر", ["HotelRoom"] = "الغرفة", ["PoolBeach"] = "المسبح/الشاطئ"
    };

    private static string Status(string english) => OrderStatuses.GetValueOrDefault(english, english);

    /// <summary>A generic Arabic message for every AppException code the API throws.</summary>
    public static readonly IReadOnlyDictionary<string, string> ByCode = new Dictionary<string, string>
    {
        ["already_done"] = "وصل هذا الأمر إلى نظام نقاط البيع مسبقًا.",
        ["already_resolved"] = "تمت معالجة هذا الفرق مسبقًا.",
        ["amount_changed"] = "تغيّر المبلغ المطلوب دفعه. يُرجى مراجعة المبلغ الجديد.",
        ["amount_required"] = "أدخل المبلغ الذي تريد دفعه.",
        ["amount_too_high"] = "هذا المبلغ أكبر من المتبقي للدفع.",
        ["area_not_found"] = "المنطقة غير موجودة.",
        ["bill_busy"] = "قام أحد الجالسين على طاولتك بتعديل الفاتورة للتو. يُرجى المحاولة مرة أخرى.",
        ["bill_changed"] = "تغيّرت الفاتورة. يُرجى مراجعة المبلغ الجديد.",
        ["cart_invalid"] = "لا يمكن طلب بعض الأصناف حاليًا.",
        ["category_not_found"] = "الفئة غير موجودة.",
        ["contact_exists"] = "هذا الرقم يتلقى التنبيهات مسبقًا.",
        ["contact_not_found"] = "هذا الرقم غير موجود في القائمة.",
        ["delivery_not_found"] = "الرسالة غير موجودة.",
        ["discrepancy_not_found"] = "الفرق غير موجود.",
        ["email_failed"] = "تعذّر إرسال البريد الإلكتروني. حاول مرة أخرى، أو شارك الرابط بطريقة أخرى.",
        ["equal_unavailable"] = "طاولتك تدفع حسب الصنف، لذا لا يمكن تقسيم الفاتورة بالتساوي.",
        ["future_day"] = "هذا اليوم لم يأتِ بعد.",
        ["image_empty"] = "اختر صورة لرفعها.",
        ["image_too_large"] = "يجب ألا يتجاوز حجم الصورة 5 ميغابايت.",
        ["image_type"] = "ارفع صورة بصيغة JPEG أو PNG أو WebP.",
        ["invalid_contact"] = "رقم الهاتف غير صالح.",
        ["invalid_credentials"] = "البريد الإلكتروني أو كلمة المرور غير صحيحة.",
        ["invalid_feedback"] = "تعذّر حفظ الملاحظات. يُرجى مراجعة البيانات المُدخلة.",
        ["invalid_mapping"] = "إعدادات الربط مع نظام نقاط البيع غير صالحة.",
        ["invalid_menu"] = "بيانات القائمة غير صالحة.",
        ["invalid_order"] = "الترتيب غير صالح.",
        ["invalid_payment"] = "تعذّر بدء عملية الدفع. يُرجى مراجعة التفاصيل والمحاولة مرة أخرى.",
        ["invalid_payment_link"] = "بيانات رابط الدفع غير صالحة.",
        ["invalid_pos"] = "إعدادات نظام نقاط البيع غير صالحة.",
        ["invalid_promo"] = "بيانات الرمز الترويجي غير صالحة.",
        ["invalid_range"] = "الفترة المختارة غير صالحة.",
        ["invalid_request"] = "الطلب غير صالح.",
        ["invalid_review"] = "التقييم غير صالح.",
        ["invalid_schedule"] = "جدول التقرير غير صالح.",
        ["invalid_session"] = "يُرجى تسجيل الدخول مرة أخرى.",
        ["invalid_settings"] = "الإعدادات غير صالحة.",
        ["invalid_settlement_file"] = "تعذّرت قراءة ملف التسوية. تحقّق من الملف وحاول مرة أخرى.",
        ["invalid_split"] = "طريقة التقسيم غير صالحة.",
        ["invalid_time"] = "الوقت غير صالح. استخدم الصيغة HH:mm، مثل 07:30.",
        ["invalid_timezone"] = "المنطقة الزمنية غير معروفة. استخدم اسمًا من قاعدة IANA مثل Asia/Amman.",
        ["invalid_transition"] = "لا يمكن نقل الطلب إلى هذه الحالة.",
        ["invalid_webhook"] = "إشعار الدفع غير صالح.",
        ["item_not_found"] = "الصنف غير موجود.",
        ["items_unavailable"] = "تم دفع جزء من هذه الفاتورة كمبلغ، لذا لم يعد ممكنًا تقسيمها حسب الصنف.",
        ["link_cancelled"] = "رابط الدفع هذا لم يعد فعّالًا.",
        ["link_expired"] = "انتهت صلاحية رابط الدفع هذا.",
        ["link_not_found"] = "رابط الدفع هذا غير صالح. تأكّد من أن لديك الرابط كاملًا.",
        ["link_paid"] = "تم دفع هذا الرابط بالكامل مسبقًا.",
        ["location_exists"] = "يوجد موقع بهذا الاسم في هذا الفرع مسبقًا.",
        ["location_not_found"] = "الموقع غير موجود.",
        ["menu_not_found"] = "القائمة غير موجودة.",
        ["menu_unavailable"] = "هذا المكان لا يوفّر القائمة الرقمية.",
        ["method_unavailable"] = "طريقة الدفع هذه غير متاحة هنا.",
        ["no_contact"] = "لم يترك الضيف بيانات للتواصل.",
        ["no_contacts"] = "أضف رقمًا أولًا.",
        ["no_items_selected"] = "انتهت صلاحية اختيارك. اختر الأصناف التي تدفع ثمنها مرة أخرى.",
        ["no_public_review"] = "لم يُعرض على هذا التقييم رابط للمراجعة العامة.",
        ["not_failed"] = "يمكن إعادة إرسال الرسائل التي فشل إرسالها فقط.",
        ["not_found"] = "غير موجود.",
        ["not_paid"] = "يتوفر الإيصال بعد إتمام عملية الدفع.",
        ["not_sandbox"] = "مزوّد الدفع التجريبي وحده يمكنه إنشاء دفعة تسوية.",
        ["nothing_to_pay"] = "لا يوجد مبلغ متبقٍ للدفع على هذه الفاتورة.",
        ["nothing_to_settle"] = "تم تحويل كل ما حصّله مزوّد الدفع التجريبي مسبقًا.",
        ["order_not_found"] = "الطلب غير موجود.",
        ["order_paid"] = "دفع الضيوف، أو يدفعون الآن، ثمن أصناف في هذا الطلب. استرد المبلغ بدلًا من رفض الطلب.",
        ["parent_not_found"] = "الفئة الرئيسية ليست في هذا الفرع.",
        ["pay_unavailable"] = "هذا المكان لا يستقبل المدفوعات من الطاولة.",
        ["payment_in_progress"] = "هناك من يدفع المبلغ المتبقي الآن. يُرجى الانتظار قليلًا.",
        ["payment_link_closed"] = "هذا الرابط لم يعد فعّالًا، لذا لا يوجد ما يمكن إرساله.",
        ["payment_link_not_found"] = "رابط الدفع غير موجود.",
        ["payment_link_paid"] = "تم دفع هذا الرابط بالكامل، لذا لا يمكن إلغاء تفعيله.",
        ["payment_not_found"] = "عملية الدفع غير موجودة.",
        ["pos_command_not_found"] = "غير موجود.",
        ["pos_not_connected"] = "اربط نظام نقاط بيع أولًا.",
        ["price_changed"] = "تغيّرت الأسعار منذ أن أضفت هذه الأصناف. يُرجى مراجعة طلبك.",
        ["promo_exists"] = "يوجد رمز ترويجي بهذا الاسم مسبقًا.",
        ["promo_expired"] = "انتهت صلاحية هذا الرمز.",
        ["promo_inactive"] = "هذا الرمز غير مفعّل.",
        ["promo_invalid"] = "هذا الرمز غير صالح هنا.",
        ["promo_min_spend"] = "فاتورتك أقل من الحد الأدنى لهذا الرمز.",
        ["promo_not_found"] = "العرض الترويجي غير موجود.",
        ["promo_not_started"] = "هذا الرمز غير صالح بعد.",
        ["promo_used_up"] = "تم استخدام هذا الرمز بالكامل.",
        ["provider_not_found"] = "مزوّد الدفع غير معروف.",
        ["qr_invalid"] = "رمز QR هذا غير مفعّل. يُرجى طلب المساعدة من أحد الموظفين.",
        ["qr_not_found"] = "لا يوجد رمز QR لهذا الموقع. جدّد الرمز لإصدار رمز جديد.",
        ["refund_not_allowed"] = "لا يمكن استرداد هذا المبلغ.",
        ["review_not_found"] = "التقييم غير موجود.",
        ["reviews_disabled"] = "هذا المكان لا يجمع التقييمات حاليًا.",
        ["schedule_not_found"] = "جدول التقرير غير موجود.",
        ["session_expired"] = "انتهت جلستك. امسح رمز QR مرة أخرى.",
        ["session_not_found"] = "الجلسة غير موجودة.",
        ["settlement_exists"] = "تم استيراد هذه الدفعات مسبقًا. لم يتم تغيير أي شيء.",
        ["settlement_not_found"] = "التسوية غير موجودة.",
        ["split_disabled"] = "يجب دفع هذا الرابط بالكامل.",
        ["split_not_set"] = "اختر أولًا عدد الأشخاص الذين يتقاسمون الفاتورة.",
        ["tenant_required"] = "سجّل الدخول إلى حساب تاجر أولًا.",
        ["terms_required"] = "يُرجى الموافقة على الشروط والأحكام لإتمام الدفع.",
        ["tip_too_high"] = "لا يمكن أن تتجاوز الإكرامية 20% من المبلغ الذي تدفعه.",
        ["tips_disabled"] = "هذا المكان لا يقبل الإكراميات عبر التطبيق.",
        ["token_required"] = "أضف رمز الوصول من نظام نقاط البيع لإتمام الربط.",
        ["too_deep"] = "لا يمكن أن تحتوي الفئات الفرعية على فئات فرعية خاصة بها.",
        ["too_many_contacts"] = "وصلت إلى الحد الأقصى لعدد الأرقام التي تتلقى التنبيهات.",
        ["too_many_points"] = "اختر عرضًا أقل تفصيلًا لهذه الفترة، مثل يومي أو أسبوعي.",
        ["transaction_not_found"] = "المعاملة غير موجودة.",
        ["unknown_pos"] = "لا يتوفر في DineFlow موصل لنظام نقاط البيع هذا.",
        ["venue_not_found"] = "الفرع غير موجود.",
        ["venue_unavailable"] = "هذا المكان لا يستقبل الطلبات أو المدفوعات حاليًا."
    };

    /// <summary>
    /// Arabic keyed by the exact English message: every fixed DomainException message in DineFlow.Domain,
    /// plus the messages of codes that are thrown with more than one wording.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ByMessage = new Dictionary<string, string>
    {
        // Application messages that differ from their code's generic text
        ["Not found."] = "غير موجود.",
        ["Scan the QR code again."] = "امسح رمز QR مرة أخرى.",
        ["There's nothing to split yet."] = "لا يوجد ما يمكن تقسيمه بعد.",
        ["This venue isn't taking orders right now."] = "هذا المكان لا يستقبل الطلبات حاليًا.",
        ["This venue isn't taking payments right now. Please contact them."] = "هذا المكان لا يستقبل المدفوعات حاليًا. يُرجى التواصل معهم.",
        ["This venue doesn't take orders from the digital menu."] = "هذا المكان لا يستقبل الطلبات من القائمة الرقمية.",
        ["This link doesn't take a tip."] = "هذا الرابط لا يقبل الإكرامية.",
        ["Choose how you want to pay."] = "اختر طريقة الدفع.",
        ["Choose how many people are splitting and how many you're paying for."] = "اختر عدد الأشخاص المشاركين في الدفع وعدد من تدفع عنهم.",
        ["Someone is paying the rest of this link right now. Please wait a moment."] = "هناك من يدفع المبلغ المتبقي من هذا الرابط الآن. يُرجى الانتظار قليلًا.",
        ["Someone at your table is paying the rest of the bill right now. Please wait a moment."] = "أحد الجالسين على طاولتك يدفع المبلغ المتبقي من الفاتورة الآن. يُرجى الانتظار قليلًا.",
        ["Every share has been paid or is being paid."] = "تم دفع جميع الحصص أو يجري دفعها.",
        ["Only one share is left to pay."] = "تبقّت حصة واحدة فقط للدفع.",
        ["You can rate your visit once the payment has gone through."] = "يمكنك تقييم زيارتك بعد إتمام عملية الدفع.",
        ["DineFlow no longer has a connector for this POS."] = "لم يعد لدى DineFlow موصل لنظام نقاط البيع هذا.",
        ["Some of those entries no longer exist. Reload and try again."] = "بعض هذه العناصر لم تعد موجودة. أعد تحميل الصفحة وحاول مرة أخرى.",
        ["Some of those items no longer exist. Reload and try again."] = "بعض هذه الأصناف لم تعد موجودة. أعد تحميل الصفحة وحاول مرة أخرى.",
        ["Only entries in the same list can be reordered together."] = "لا يمكن إعادة الترتيب معًا إلا للعناصر الموجودة في القائمة نفسها.",
        ["Each id may appear only once."] = "يجب أن يظهر كل عنصر مرة واحدة فقط.",
        ["That area doesn't belong to this venue."] = "هذه المنطقة لا تتبع هذا الفرع.",
        ["That category belongs to a different venue."] = "هذه الفئة تتبع فرعًا آخر.",
        ["'To' must be greater than or equal to 'From'."] = "يجب أن يكون «إلى» أكبر من «من» أو مساويًا له.",
        ["Create at most 200 tables at a time."] = "يمكنك إنشاء 200 طاولة كحد أقصى في المرة الواحدة.",
        ["Choose up to a year, start before end."] = "اختر فترة لا تتجاوز سنة، تبدأ قبل نهايتها.",
        ["The start of the range must be before its end."] = "يجب أن تكون بداية الفترة قبل نهايتها.",
        ["Choose a range of a year or less."] = "اختر فترة لا تتجاوز سنة.",
        ["Choose a CSV settlement report up to 5 MB."] = "اختر تقرير تسوية بصيغة CSV لا يتجاوز حجمه 5 ميغابايت.",
        ["The file has no rows under the header."] = "لا يحتوي الملف على صفوف تحت سطر العناوين.",
        ["The file needs an \"order id\" or \"reference\" column."] = "يجب أن يحتوي الملف على عمود \"order id\" أو \"reference\".",

        // DineFlow.Domain
        ["This bill is already linked to a different POS check."] = "هذه الفاتورة مرتبطة مسبقًا بفاتورة أخرى في نظام نقاط البيع.",
        ["Someone has already paid a share, so the split can't be removed. Pay the rest in full instead."] = "دفع أحدهم حصته مسبقًا، لذا لا يمكن إلغاء التقسيم. ادفع المتبقي بالكامل بدلًا من ذلك.",
        ["This item is held by another guest."] = "هذا الصنف محجوز لضيف آخر.",
        ["This item is already being paid for."] = "يجري دفع ثمن هذا الصنف حاليًا.",
        ["This has already been resolved."] = "تمت معالجة هذا مسبقًا.",
        ["Settlement amounts and fees must be positive; the line type says which way they go."] = "يجب أن تكون مبالغ التسوية والرسوم موجبة؛ ويحدد نوع السطر اتجاهها.",
        ["Pick at least one day for the menu."] = "اختر يومًا واحدًا على الأقل للقائمة.",
        ["Set both a start and an end time, or neither for all day."] = "حدّد وقتَي البدء والانتهاء معًا، أو اتركهما فارغين لتكون القائمة طوال اليوم.",
        ["Start and end time can't be the same."] = "لا يمكن أن يكون وقت البدء ووقت الانتهاء متطابقين.",
        ["Price can't be negative."] = "لا يمكن أن يكون السعر سالبًا.",
        ["A modifier group no longer exists. Reload the item and try again."] = "إحدى مجموعات الإضافات لم تعد موجودة. أعد تحميل الصنف وحاول مرة أخرى.",
        ["A modifier option no longer exists. Reload the item and try again."] = "أحد خيارات الإضافات لم يعد موجودًا. أعد تحميل الصنف وحاول مرة أخرى.",
        ["Option prices can't be negative."] = "لا يمكن أن تكون أسعار الخيارات سالبة.",
        ["Enter the number in international format, starting with + and the country code, e.g. +962 79 123 4567."] = "أدخل الرقم بالصيغة الدولية، بدءًا بـ + ورمز الدولة، مثل ‎+962 79 123 4567.",
        ["This message isn't waiting to be sent."] = "هذه الرسالة ليست بانتظار الإرسال.",
        ["Only a failed message can be sent again."] = "يمكن إعادة إرسال الرسائل التي فشل إرسالها فقط.",
        ["Choose an hour between 00:00 and 23:00."] = "اختر ساعة بين 00:00 و23:00.",
        ["Add at least one e-mail address."] = "أضف عنوان بريد إلكتروني واحدًا على الأقل.",
        ["An order needs at least one item."] = "يجب أن يحتوي الطلب على صنف واحد على الأقل.",
        ["Only confirmed refunds go in the ledger."] = "لا تُسجَّل في الدفتر إلا عمليات الاسترداد المؤكدة.",
        ["Only successful payments go in the ledger."] = "لا تُسجَّل في الدفتر إلا عمليات الدفع الناجحة.",
        ["A payment link can't be paid by item."] = "لا يمكن دفع رابط الدفع حسب الصنف.",
        ["There's nothing to pay."] = "لا يوجد ما يمكن دفعه.",
        ["An equal-split payment needs its shares and the number of people."] = "يتطلب الدفع بالتقسيم المتساوي عدد الحصص وعدد الأشخاص.",
        ["The discount can't be more than the bill."] = "لا يمكن أن يتجاوز الخصم قيمة الفاتورة.",
        ["The tip can't be negative."] = "لا يمكن أن تكون الإكرامية سالبة.",
        ["The tip can't be more than 20% of the bill."] = "لا يمكن أن تتجاوز الإكرامية 20% من قيمة الفاتورة.",
        ["Enter the amount to collect."] = "أدخل المبلغ المطلوب تحصيله.",
        ["That amount is too large for a payment link."] = "هذا المبلغ كبير جدًا لرابط دفع.",
        ["A link can be active for 1 hour up to 90 days."] = "يمكن أن يبقى الرابط فعّالًا من ساعة واحدة حتى 90 يومًا.",
        ["This link has been paid in full, so there's nothing to deactivate."] = "تم دفع هذا الرابط بالكامل، لذا لا يوجد ما يمكن إلغاء تفعيله.",
        ["Only a successful payment can be refunded."] = "لا يمكن استرداد إلا عملية دفع ناجحة.",
        ["Enter an amount to refund."] = "أدخل المبلغ المراد استرداده.",
        ["This payment has already been fully refunded."] = "تم استرداد هذه الدفعة بالكامل مسبقًا.",
        ["That's more than is left to refund on this payment."] = "هذا المبلغ أكبر من المتبقي القابل للاسترداد من هذه الدفعة.",
        ["This command isn't waiting to be sent."] = "هذا الأمر ليس بانتظار الإرسال.",
        ["This has already reached the POS."] = "وصل هذا إلى نظام نقاط البيع مسبقًا.",
        ["Codes are 3–20 letters, digits or dashes, e.g. WELCOME10."] = "يتكوّن الرمز من 3 إلى 20 حرفًا أو رقمًا أو شَرطة، مثل WELCOME10.",
        ["A percentage discount must be between 0.01% and 100%."] = "يجب أن تكون نسبة الخصم بين 0.01% و100%.",
        ["A fixed discount must be more than zero."] = "يجب أن يكون الخصم الثابت أكبر من صفر.",
        ["The end date must be after the start date."] = "يجب أن يكون تاريخ الانتهاء بعد تاريخ البدء.",
        ["The redemption limit must be at least 1."] = "يجب أن يكون حد الاستخدام 1 على الأقل.",
        ["Limits can't be negative."] = "لا يمكن أن تكون الحدود سالبة.",
        ["Choose between 1 and 5 stars."] = "اختر من نجمة واحدة إلى 5 نجوم.",
        ["Private feedback is for ratings of 1 to 3 stars."] = "الملاحظات الخاصة متاحة للتقييمات من نجمة إلى 3 نجوم.",
        ["Please agree to be contacted, or leave the contact details empty."] = "يُرجى الموافقة على التواصل معك، أو ترك بيانات التواصل فارغة.",
        ["Leave an email address or a phone number so the team can reach you."] = "اترك بريدًا إلكترونيًا أو رقم هاتف حتى يتمكن الفريق من التواصل معك.",
        ["This review wasn't offered a public review link."] = "لم يُعرض على هذا التقييم رابط للمراجعة العامة.",
        ["Area name is required."] = "اسم المنطقة مطلوب.",
        ["A retired QR code cannot be re-enabled."] = "لا يمكن إعادة تفعيل رمز QR تم إيقافه نهائيًا.",
        ["Location label is required."] = "اسم الموقع مطلوب.",
        ["Add the link to your review page, or choose no public review platform."] = "أضف رابط صفحة المراجعات الخاصة بك، أو اختر عدم استخدام منصة مراجعات عامة.",
        ["The review link must be a full https:// address."] = "يجب أن يكون رابط المراجعات عنوانًا كاملًا يبدأ بـ https://.",
        ["Offer between one and four tip suggestions."] = "اعرض من اقتراح واحد إلى أربعة اقتراحات للإكرامية.",
        ["Tip suggestions must be between 0.01% and 20%."] = "يجب أن تكون اقتراحات الإكرامية بين 0.01% و20%.",
        ["The most common tip must be one of the suggestions."] = "يجب أن تكون الإكرامية الأكثر اختيارًا من ضمن الاقتراحات.",
        ["Service charge must be between 0% and 30%."] = "يجب أن تكون رسوم الخدمة بين 0% و30%.",
        ["Tax must be between 0% and 30%."] = "يجب أن تكون الضريبة بين 0% و30%.",
        ["Venue name is required."] = "اسم الفرع مطلوب.",
        ["Currency must be a 3-letter ISO 4217 code."] = "يجب أن تكون العملة رمزًا من 3 أحرف وفق ISO 4217.",
        ["Enable the menu, Pay at Table, or both, so guests have something to do."] = "فعّل القائمة أو الدفع من الطاولة أو كليهما، حتى يتمكن الضيوف من استخدام الخدمة."
    };

    /// <summary>Interpolated messages: the pattern matches the whole English text and builds the Arabic from its parts.</summary>
    private static readonly (Regex Pattern, Func<Match, string> Arabic)[] Patterns =
    [
        // Domain
        (P(@"^(.+) is required\.$"), m => $"حقل «{Field(m.Groups[1].Value)}» مطلوب."),
        (P(@"^(.+) must be (\d+) characters or fewer\.$"), m => $"يجب ألا يتجاوز «{Field(m.Groups[1].Value)}» {m.Groups[2].Value} حرفًا."),
        (P(@"^Split between 2 and (\d+) people\.$"), m => $"يجب أن يكون عدد الأشخاص بين 2 و{m.Groups[1].Value}."),
        (P(@"^(\d+) shares? (?:has|have) already been paid, so split between at least (\d+) people\.$"),
            m => $"تم دفع {m.Groups[1].Value} من الحصص مسبقًا، لذا يجب التقسيم على {m.Groups[2].Value} أشخاص على الأقل."),
        (P(@"^Batch (.+) has no transactions\.$"), m => $"الدفعة {m.Groups[1].Value} لا تحتوي على أي معاملات."),
        (P(@"^Batch (.+) has more than one payout date\.$"), m => $"الدفعة {m.Groups[1].Value} لها أكثر من تاريخ تحويل."),
        (P(@"^An item can have at most (\d+) modifier groups\.$"), m => $"يمكن أن يحتوي الصنف على {m.Groups[1].Value} مجموعات إضافات كحد أقصى."),
        (P(@"^Use at most (\d+) tags\.$"), m => $"استخدم {m.Groups[1].Value} وسوم كحد أقصى."),
        (P(@"^Tags must be (\d+) characters or fewer\.$"), m => $"يجب ألا يتجاوز طول الوسم {m.Groups[1].Value} حرفًا."),
        (P(@"^""(.+)"" needs at least one option\.$"), m => $"تحتاج المجموعة \"{m.Groups[1].Value}\" إلى خيار واحد على الأقل."),
        (P(@"^""(.+)"" can have at most (\d+) options\.$"), m => $"يمكن أن تحتوي المجموعة \"{m.Groups[1].Value}\" على {m.Groups[2].Value} خيارات كحد أقصى."),
        (P(@"^""(.+)"": pick limits must satisfy .+$"),
            m => $"\"{m.Groups[1].Value}\": يجب أن يكون الحد الأدنى للاختيار بين 0 والحد الأقصى، وأن يكون الحد الأقصى 1 على الأقل."),
        (P(@"^""(.+)"": maximum picks can't be more than the number of options\.$"),
            m => $"\"{m.Groups[1].Value}\": لا يمكن أن يتجاوز الحد الأقصى للاختيار عدد الخيارات."),
        (P(@"^""(.+)"": more options are pre-selected than guests are allowed to pick\.$"),
            m => $"\"{m.Groups[1].Value}\": عدد الخيارات المحددة مسبقًا أكبر مما يُسمح للضيوف باختياره."),
        (P(@"^Send to (\d+) addresses or fewer\.$"), m => $"أرسل إلى {m.Groups[1].Value} عناوين أو أقل."),
        (P(@"^""(.+)"" isn't a valid e-mail address\.$"), m => $"\"{m.Groups[1].Value}\" ليس عنوان بريد إلكتروني صالحًا."),
        (P(@"^An order can have at most (\d+) lines\.$"), m => $"يمكن أن يحتوي الطلب على {m.Groups[1].Value} سطرًا كحد أقصى."),
        (P(@"^An order that is (\w+) can't become (\w+)\.$"),
            m => $"لا يمكن نقل طلب حالته «{Status(m.Groups[1].Value)}» إلى «{Status(m.Groups[2].Value)}»."),
        (P(@"^Quantity must be between 1 and (\d+)\.$"), m => $"يجب أن تكون الكمية بين 1 و{m.Groups[1].Value}."),

        // Application
        (P(@"^Only (\d+) shares are left to pay\.$"), m => $"الحصص المتبقية للدفع: {m.Groups[1].Value} فقط."),
        (P(@"^Guests have paid, or are paying, for items on order #(\d+)\. Refund it instead of rejecting it\.$"),
            m => $"دفع الضيوف، أو يدفعون الآن، ثمن أصناف في الطلب #{m.Groups[1].Value}. استرد المبلغ بدلًا من رفض الطلب."),
        (P(@"^The table has already paid for part of order #(\d+)\. Refund it instead of rejecting it\.$"),
            m => $"دفعت الطاولة جزءًا من الطلب #{m.Groups[1].Value} مسبقًا. استرد المبلغ بدلًا من رفض الطلب."),
        (P(@"^Order #(\d+) is (\w+) and can't become (\w+)\.$"),
            m => $"لا يمكن نقل الطلب #{m.Groups[1].Value} من حالة «{Status(m.Groups[2].Value)}» إلى «{Status(m.Groups[3].Value)}»."),
        (P(@"^Already imported: (.+)\. Nothing was changed\.$"), m => $"تم استيرادها مسبقًا: {m.Groups[1].Value}. لم يتم تغيير أي شيء."),
        (P(@"^'(.*)' isn't a valid time\. Use HH:mm, e\.g\. 07:30\.$"), m => $"'{m.Groups[1].Value}' ليس وقتًا صالحًا. استخدم الصيغة HH:mm، مثل 07:30."),
        (P(@"^'(.*)' is not a known timezone\. .+$"), m => $"'{m.Groups[1].Value}' ليست منطقة زمنية معروفة. استخدم اسمًا من قاعدة IANA مثل Asia/Amman."),
        (P(@"^Up to (\d+) numbers can get alerts\.$"), m => $"يمكن إضافة {m.Groups[1].Value} أرقام كحد أقصى لتلقي التنبيهات."),
        (P(@"^There's already a code (.+)\.$"), m => $"يوجد رمز ترويجي باسم {m.Groups[1].Value} مسبقًا."),
        (P(@"^Add the access token from (.+) to connect\.$"), m => $"أضف رمز الوصول من {m.Groups[1].Value} لإتمام الربط."),
        (P(@"^(Table|Counter|HotelRoom|PoolBeach) (.+) already exists at this venue\.$"),
            m => $"يوجد مسبقًا موقع باسم {m.Groups[2].Value} من نوع «{LocationTypes[m.Groups[1].Value]}» في هذا الفرع."),
        // Settlement CSV
        (P(@"^The file needs a ""(.+)"" column\.$"), m => $"يجب أن يحتوي الملف على عمود \"{m.Groups[1].Value}\"."),
        (P(@"^Line (\d+): ""(.*)"" is empty\.$"), m => $"السطر {m.Groups[1].Value}: الحقل \"{m.Groups[2].Value}\" فارغ."),
        (P(@"^Line (\d+): the currency is (.+), but this venue uses (.+)\.$"),
            m => $"السطر {m.Groups[1].Value}: العملة {m.Groups[2].Value}، لكن هذا الفرع يستخدم {m.Groups[3].Value}."),
        (P(@"^Line (\d+): ""(.*)"" isn't a type DineFlow knows .+$"),
            m => $"السطر {m.Groups[1].Value}: النوع \"{m.Groups[2].Value}\" غير معروف (استخدم payment أو refund أو fee)."),
        (P(@"^Line (\d+): ""(.*)"" isn't an amount\.$"), m => $"السطر {m.Groups[1].Value}: \"{m.Groups[2].Value}\" ليس مبلغًا صالحًا."),
        (P(@"^Line (\d+): ""(.*)"" has more decimals than the currency allows\.$"),
            m => $"السطر {m.Groups[1].Value}: يحتوي \"{m.Groups[2].Value}\" على منازل عشرية أكثر مما تسمح به العملة."),
        (P(@"^Line (\d+): ""(.*)"" isn't a date .+$"),
            m => $"السطر {m.Groups[1].Value}: \"{m.Groups[2].Value}\" ليس تاريخًا صالحًا (استخدم الصيغة yyyy-MM-dd).")
    ];

    // Model validation (data annotations and JSON binding). The errors dictionary already names the field.
    private static readonly Dictionary<string, string> ValidationMessages = new()
    {
        ["Use HH:mm, e.g. 07:30."] = "استخدم الصيغة HH:mm، مثل 07:30.",
        ["Use HH:mm, e.g. 11:30."] = "استخدم الصيغة HH:mm، مثل 11:30.",
        ["Enter a phone number with digits only."] = "أدخل رقم هاتف بالأرقام فقط.",
        ["Use a 3-letter ISO 4217 currency code."] = "استخدم رمز عملة من 3 أحرف وفق ISO 4217.",
        ["Supported languages are en and ar."] = "اللغات المدعومة هي en وar.",
        ["Use a hex colour such as #0B3B38."] = "استخدم لونًا بصيغة hex مثل #0B3B38."
    };

    private static readonly (Regex Pattern, Func<Match, string> Arabic)[] ValidationPatterns =
    [
        (P(@"^The .+ field is required\.$"), _ => "هذا الحقل مطلوب."),
        (P(@"^The field .+ must be between (.+) and (.+)\.$"), m => $"يجب أن تكون القيمة بين {m.Groups[1].Value} و{m.Groups[2].Value}."),
        (P(@"^The field .+ must be a string with a minimum length of (\d+) and a maximum length of (\d+)\.$"),
            m => $"يجب أن يكون الطول بين {m.Groups[1].Value} و{m.Groups[2].Value} حرفًا."),
        (P(@"^The field .+ must be a string with a maximum length of (\d+)\.$"), m => $"يجب ألا يتجاوز الطول {m.Groups[1].Value} حرفًا."),
        (P(@"^The field .+ must be a string or array type with a minimum length of '(\d+)'\.$"), m => $"يجب ألا يقل العدد أو الطول عن {m.Groups[1].Value}."),
        (P(@"^The field .+ must be a string or array type with a maximum length of '(\d+)'\.$"), m => $"يجب ألا يتجاوز العدد أو الطول {m.Groups[1].Value}."),
        (P(@"^The .+ field is not a valid e-mail address\.$"), _ => "عنوان البريد الإلكتروني غير صالح."),
        (P(@"^The .+ field is not a valid fully-qualified http, https, or ftp URL\.$"), _ => "الرابط غير صالح. أدخل عنوانًا كاملًا يبدأ بـ https://."),
        (P(@"^The field .+ must match the regular expression .+$"), _ => "الصيغة غير صحيحة."),
        (P(@"^The JSON value could not be converted to .+$"), _ => "القيمة غير صالحة."),
        (P(@"^.*(?:JSON|is an invalid start of a value|Path:).*$"), _ => "تعذّرت قراءة البيانات المُرسلة.")
    ];
}
