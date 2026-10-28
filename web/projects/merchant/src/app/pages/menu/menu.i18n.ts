import { Lexicon } from '../../core/i18n';

/** "1 item" / "3 items". */
function count(n: number, one: string, many: string): string {
  return n === 1 ? `1 ${one}` : `${n} ${many}`;
}

/**
 * An Arabic count: the singular and dual stand alone ("صنف واحد", "صنفان"); 3–10 take the
 * plural ("3 أصناف"); 0, 11 and up take the singular ("15 صنف").
 */
function arCount(n: number, one: string, two: string, few: string, many: string): string {
  if (n === 1) return one;
  if (n === 2) return two;
  const rest = n % 100;
  return rest >= 3 && rest <= 10 ? `${n} ${few}` : `${n} ${many}`;
}

const en = {
  // Shared by the editors
  name: 'Name',
  arabicName: 'Arabic name',
  optional: '(optional)',
  enterName: 'Enter a name.',
  days: {
    Monday: 'Mon', Tuesday: 'Tue', Wednesday: 'Wed', Thursday: 'Thu', Friday: 'Fri', Saturday: 'Sat', Sunday: 'Sun'
  } as Record<string, string>,
  allergens: {
    Gluten: 'Gluten', Crustaceans: 'Crustaceans', Eggs: 'Eggs', Fish: 'Fish', Peanuts: 'Peanuts', Soy: 'Soy',
    Milk: 'Milk / dairy', TreeNuts: 'Tree nuts', Celery: 'Celery', Mustard: 'Mustard', Sesame: 'Sesame',
    Sulphites: 'Sulphites', Lupin: 'Lupin', Molluscs: 'Molluscs'
  } as Record<string, string>,

  // Item rows, shared by the menu and items pages
  itemCount: (n: number) => count(n, 'item', 'items'),
  optionGroups: (n: number) => count(n, 'option group', 'option groups'),
  allergenCount: (n: number) => count(n, 'allergen', 'allergens'),
  available: 'Available',
  soldOut: 'Sold out',
  markSoldOut: (name: string) => `Mark sold out: ${name}`,
  markAvailable: (name: string) => `Mark available: ${name}`,
  availabilityChanged: (name: string, available: boolean) =>
    `"${name}" is ${available ? 'back on the menu' : 'marked sold out'}.`,
  saved: (name: string) => `Saved "${name}".`,
  deleted: (name: string) => `Deleted "${name}".`,

  /** The menu page (menu-admin). */
  admin: {
    title: 'Menu',
    intro: 'The menus guests see when they scan a QR code, and the items on each.',
    newMenu: 'New menu',
    noMenus: 'No menus yet.',
    createOneWith: 'Create one with',
    createOneExample: ', for example "All day" or "Breakfast".',
    draftSuffix: '(draft)',
    published: 'Published',
    draft: 'Draft',
    servingNow: 'Serving now',
    notServingNow: 'Not serving now',
    linkItems: 'Link items',
    editMenu: 'Edit menu',
    moveMenuEarlier: 'Move menu earlier',
    moveMenuLater: 'Move menu later',
    // Earlier menus sit at the start of the row, which is on the right in Arabic.
    earlierArrow: '←',
    laterArrow: '→',
    noLinkedItems: 'No items on this menu yet.',
    noLinkedItemsHint: 'Use Link items to choose them from your items and item categories.',
    // Schedule line, e.g. "Mon, Tue, 08:00–11:30"
    everyDay: 'Every day',
    listSeparator: ', ',
    allDay: (days: string) => `${days}, all day`,
    hours: (days: string, start: string, end: string) => `${days}, ${start}–${end}`,
    // Notices and confirmations
    savedMenu: (name: string) => `Saved menu "${name}".`,
    confirmDeleteMenu: (name: string) => `Delete the "${name}" menu? Its items stay under Items.`,
    deletedMenu: (name: string) => `Deleted menu "${name}".`,
    linked: (menu: string, n: number) => `"${menu}" now shows ${count(n, 'item', 'items')}.`
  },

  /** The item categories page. */
  categoriesPage: {
    title: 'Item Categories',
    intro: 'Group your items, e.g. Starters, Mains and Drinks. A category can have one level of subcategories.',
    newCategory: 'New category',
    noCategories: 'No categories yet.',
    noCategoriesHint: 'Add one with New category, for example "Starters" or "Drinks".',
    viewItems: 'View items',
    addSubcategory: '+ Subcategory',
    rename: 'Rename',
    moveUp: 'Move category up',
    moveDown: 'Move category down',
    delete: 'Delete category',
    renameCategory: 'Rename category',
    newSubcategory: 'New subcategory',
    savedCategory: (name: string) => `Saved category "${name}".`,
    confirmDelete: (name: string, items: number) =>
      `Delete "${name}"${items ? ` and its ${count(items, 'item', 'items')}` : ''}? They come off every menu. This can't be undone.`
  },

  /** The items page. */
  itemsPage: {
    title: 'Items',
    intro: 'Every dish and drink, with prices, photos and options. Put them on a menu with Link items on the Menu page.',
    newItem: 'New item',
    search: 'Search items',
    category: 'Category',
    allCategories: 'All categories',
    noCategories: 'Add a category first.',
    noCategoriesHint: 'Items sit in categories such as Starters or Drinks.',
    goToCategories: 'Go to Item Categories',
    noItems: 'No items yet.',
    noItemsHint: 'Add one with New item.',
    noMatches: 'No items match.',
    item: 'Item',
    menus: 'Menus',
    price: 'Price',
    status: 'Status',
    actions: 'Actions',
    notOnAnyMenu: 'Not on any menu',
    moveUp: 'Move item up',
    moveDown: 'Move item down',
    delete: 'Delete item',
    confirmDelete: (name: string) => `Delete "${name}"? It comes off every menu. This can't be undone.`
  },

  /** The link items dialog (link-items). */
  link: {
    title: (menu: string) => `Link items to "${menu}"`,
    intro: 'Tick the items this menu shows. Tick a category to take all of its items.',
    search: 'Search items',
    noItems: 'You have no items yet.',
    noItemsHint: 'Add them under Items first.',
    noMatches: 'No items match.',
    allIn: (category: string) => `All items in ${category}`,
    selected: (n: number) => `${count(n, 'item', 'items')} selected`
  },

  /** The menu dialog (menu-editor). */
  menu: {
    editMenu: 'Edit menu',
    newMenu: 'New menu',
    namePlaceholder: 'e.g. Breakfast',
    days: 'Days',
    hours: 'Hours',
    allDay: 'All day',
    startTime: 'Start time',
    to: 'to',
    endTime: 'End time',
    hoursHint: 'Venue local time. An end before the start runs past midnight.',
    published: 'Published',
    publishedHint: 'Guests only see published menus.',
    saveMenu: 'Save menu',
    createMenu: 'Create menu',
    pickDay: 'Pick at least one day.',
    setTimes: 'Set a start and end time, or choose All day.',
    sameTimes: "Start and end time can't be the same."
  },

  /** The category dialog (category-editor). */
  category: {
    namePlaceholder: 'e.g. Mains'
  },

  /** The item drawer (item-editor). */
  item: {
    editItem: 'Edit item',
    newItem: 'New item',
    description: 'Description',
    arabicDescription: 'Arabic description',
    price: (currency: string) => `Price (${currency})`,
    category: 'Category',
    photo: 'Photo',
    photoAlt: 'Item photo',
    noPhoto: 'No photo',
    uploading: 'Uploading…',
    replacePhoto: 'Replace photo',
    uploadPhoto: 'Upload photo',
    removePhoto: 'Remove photo',
    photoHint: 'JPEG, PNG or WebP, up to 5 MB.',
    allergens: 'Allergens',
    tags: 'Tags',
    tagsHint: '(comma-separated)',
    tagsPlaceholder: "Vegan, Spicy, Chef's pick",
    available: 'Available',
    availableHint: 'Untick to show it as sold out.',
    modifierGroups: 'Modifier groups',
    addGroup: '+ Add group',
    noGroups: 'No options. Add a group for choices such as size, doneness or extras.',
    groupName: 'Group name',
    groupNamePlaceholder: 'e.g. Size',
    minPicks: 'Min picks',
    maxPicks: 'Max picks',
    moveGroupUp: 'Move group up',
    moveGroupDown: 'Move group down',
    removeGroup: 'Remove group',
    pickRule: (min: number, max: number) =>
      `${min > 0 ? 'Required' : 'Optional'}: guests pick ${min === max ? max : min > 0 ? `${min}–${max}` : `up to ${max}`}.`,
    option: 'Option',
    arabic: 'Arabic',
    extraPriceHeader: '+ Price',
    default: 'Default',
    inStock: 'In stock',
    optionName: 'Option name',
    arabicOptionName: 'Arabic option name',
    extraPrice: 'Extra price',
    selectedByDefault: 'Selected by default',
    removeOption: 'Remove option',
    addOption: '+ Add option',
    saveItem: 'Save item',
    addItem: 'Add item',
    // Validation
    imageTooLarge: 'Images must be 5 MB or smaller.',
    enterItemName: 'Enter the item name.',
    invalidPrice: (currency: string) => `Enter a valid price in ${currency}, e.g. 25.00.`,
    tooManyTags: 'Use at most 8 tags.',
    tagTooLong: 'Tags must be 24 characters or fewer.',
    aGroup: 'A modifier group',
    groupNeedsName: 'Every modifier group needs a name.',
    groupNeedsOption: (group: string) => `"${group}" needs at least one option.`,
    pickRange: (group: string) => `"${group}": minimum picks must be between 0 and the maximum, and the maximum at least 1.`,
    maxOverOptions: (group: string) => `"${group}": maximum picks can't be more than the number of options.`,
    tooManyDefaults: (group: string) => `"${group}": more options are pre-selected than guests may pick.`,
    optionNeedsName: (group: string) => `Every option in "${group}" needs a name.`,
    optionInvalidPrice: (option: string) => `"${option}" has an invalid price.`
  }
};

export const MENU: Lexicon<typeof en> = {
  en,
  ar: {
    name: 'الاسم',
    arabicName: 'الاسم بالعربية',
    optional: '(اختياري)',
    enterName: 'أدخل الاسم.',
    days: {
      Monday: 'الإثنين', Tuesday: 'الثلاثاء', Wednesday: 'الأربعاء', Thursday: 'الخميس', Friday: 'الجمعة', Saturday: 'السبت', Sunday: 'الأحد'
    },
    allergens: {
      Gluten: 'الغلوتين', Crustaceans: 'القشريات', Eggs: 'البيض', Fish: 'السمك', Peanuts: 'الفول السوداني', Soy: 'الصويا',
      Milk: 'الحليب / الألبان', TreeNuts: 'المكسرات', Celery: 'الكرفس', Mustard: 'الخردل', Sesame: 'السمسم',
      Sulphites: 'الكبريتيت', Lupin: 'الترمس', Molluscs: 'الرخويات'
    },

    itemCount: (n: number) => arCount(n, 'صنف واحد', 'صنفان', 'أصناف', 'صنف'),
    optionGroups: (n: number) => arCount(n, 'مجموعة إضافات واحدة', 'مجموعتا إضافات', 'مجموعات إضافات', 'مجموعة إضافات'),
    allergenCount: (n: number) => arCount(n, 'مسبب حساسية واحد', 'مسببا حساسية', 'مسببات حساسية', 'مسبب حساسية'),
    available: 'متوفر',
    soldOut: 'نفد',
    markSoldOut: (name: string) => `تحديد كنافد: ${name}`,
    markAvailable: (name: string) => `تحديد كمتوفر: ${name}`,
    availabilityChanged: (name: string, available: boolean) =>
      available ? `"${name}" متوفر مجدداً في القائمة.` : `تم تحديد "${name}" كنافد.`,
    saved: (name: string) => `تم حفظ "${name}".`,
    deleted: (name: string) => `تم حذف "${name}".`,

    admin: {
      title: 'القائمة',
      intro: 'القوائم التي يراها الضيوف عند مسح رمز QR، والأصناف في كل منها.',
      newMenu: 'قائمة جديدة',
      noMenus: 'لا توجد قوائم بعد.',
      createOneWith: 'أنشئ قائمة عبر',
      createOneExample: '، مثل "طوال اليوم" أو "الفطور".',
      draftSuffix: '(مسودة)',
      published: 'منشورة',
      draft: 'مسودة',
      servingNow: 'متاحة الآن',
      notServingNow: 'غير متاحة الآن',
      linkItems: 'ربط الأصناف',
      editMenu: 'تعديل القائمة',
      moveMenuEarlier: 'تقديم القائمة في الترتيب',
      moveMenuLater: 'تأخير القائمة في الترتيب',
      earlierArrow: '→',
      laterArrow: '←',
      noLinkedItems: 'لا توجد أصناف في هذه القائمة بعد.',
      noLinkedItemsHint: 'استخدم "ربط الأصناف" لاختيارها من أصنافك وفئات الأصناف.',
      everyDay: 'كل يوم',
      listSeparator: '، ',
      allDay: (days: string) => `${days}، طوال اليوم`,
      hours: (days: string, start: string, end: string) => `${days}، ${start}–${end}`,
      savedMenu: (name: string) => `تم حفظ القائمة "${name}".`,
      confirmDeleteMenu: (name: string) => `هل تريد حذف قائمة "${name}"؟ ستبقى أصنافها في صفحة الأصناف.`,
      deletedMenu: (name: string) => `تم حذف القائمة "${name}".`,
      linked: (menu: string, n: number) => `أصبحت قائمة "${menu}" تعرض ${arCount(n, 'صنفاً واحداً', 'صنفين', 'أصناف', 'صنفاً')}.`
    },

    categoriesPage: {
      title: 'فئات الأصناف',
      intro: 'جمّع أصنافك في فئات، مثل المقبلات والأطباق الرئيسية والمشروبات. يمكن أن تحتوي الفئة على مستوى واحد من الفئات الفرعية.',
      newCategory: 'فئة جديدة',
      noCategories: 'لا توجد فئات بعد.',
      noCategoriesHint: 'أضف فئة عبر "فئة جديدة"، مثل "المقبلات" أو "المشروبات".',
      viewItems: 'عرض الأصناف',
      addSubcategory: '+ فئة فرعية',
      rename: 'إعادة تسمية',
      moveUp: 'نقل الفئة للأعلى',
      moveDown: 'نقل الفئة للأسفل',
      delete: 'حذف الفئة',
      renameCategory: 'إعادة تسمية الفئة',
      newSubcategory: 'فئة فرعية جديدة',
      savedCategory: (name: string) => `تم حفظ الفئة "${name}".`,
      confirmDelete: (name: string, items: number) =>
        `هل تريد حذف "${name}"${items ? ` مع ${arCount(items, 'الصنف الوحيد فيها', 'الصنفين فيها', 'أصناف فيها', 'صنفاً فيها')}` : ''}؟ ستُزال من جميع القوائم، ولا يمكن التراجع عن هذا الإجراء.`
    },

    itemsPage: {
      title: 'الأصناف',
      intro: 'كل الأطباق والمشروبات بأسعارها وصورها وخياراتها. أضفها إلى قائمة عبر "ربط الأصناف" في صفحة القائمة.',
      newItem: 'صنف جديد',
      search: 'البحث في الأصناف',
      category: 'الفئة',
      allCategories: 'جميع الفئات',
      noCategories: 'أضف فئة أولاً.',
      noCategoriesHint: 'توضع الأصناف في فئات مثل المقبلات أو المشروبات.',
      goToCategories: 'الانتقال إلى فئات الأصناف',
      noItems: 'لا توجد أصناف بعد.',
      noItemsHint: 'أضف صنفاً عبر "صنف جديد".',
      noMatches: 'لا توجد أصناف مطابقة.',
      item: 'الصنف',
      menus: 'القوائم',
      price: 'السعر',
      status: 'الحالة',
      actions: 'الإجراءات',
      notOnAnyMenu: 'غير موجود في أي قائمة',
      moveUp: 'نقل الصنف للأعلى',
      moveDown: 'نقل الصنف للأسفل',
      delete: 'حذف الصنف',
      confirmDelete: (name: string) => `هل تريد حذف "${name}"؟ سيُزال من جميع القوائم، ولا يمكن التراجع عن هذا الإجراء.`
    },

    link: {
      title: (menu: string) => `ربط الأصناف بقائمة "${menu}"`,
      intro: 'حدّد الأصناف التي تعرضها هذه القائمة. حدّد فئة لإضافة جميع أصنافها.',
      search: 'البحث في الأصناف',
      noItems: 'لا توجد لديك أصناف بعد.',
      noItemsHint: 'أضفها أولاً من صفحة الأصناف.',
      noMatches: 'لا توجد أصناف مطابقة.',
      allIn: (category: string) => `جميع الأصناف في ${category}`,
      selected: (n: number) => `تم تحديد ${arCount(n, 'صنف واحد', 'صنفين', 'أصناف', 'صنفاً')}`
    },

    menu: {
      editMenu: 'تعديل القائمة',
      newMenu: 'قائمة جديدة',
      namePlaceholder: 'مثال: الفطور',
      days: 'الأيام',
      hours: 'الساعات',
      allDay: 'طوال اليوم',
      startTime: 'وقت البدء',
      to: 'إلى',
      endTime: 'وقت الانتهاء',
      hoursHint: 'بالتوقيت المحلي للفرع. إذا كان وقت الانتهاء قبل وقت البدء فستستمر القائمة إلى ما بعد منتصف الليل.',
      published: 'منشورة',
      publishedHint: 'لا يرى الضيوف إلا القوائم المنشورة.',
      saveMenu: 'حفظ القائمة',
      createMenu: 'إنشاء القائمة',
      pickDay: 'اختر يوماً واحداً على الأقل.',
      setTimes: 'حدّد وقت البدء ووقت الانتهاء، أو اختر "طوال اليوم".',
      sameTimes: 'لا يمكن أن يكون وقت البدء ووقت الانتهاء متطابقين.'
    },

    category: {
      namePlaceholder: 'مثال: الأطباق الرئيسية'
    },

    item: {
      editItem: 'تعديل الصنف',
      newItem: 'صنف جديد',
      description: 'الوصف',
      arabicDescription: 'الوصف بالعربية',
      price: (currency: string) => `السعر (${currency})`,
      category: 'الفئة',
      photo: 'الصورة',
      photoAlt: 'صورة الصنف',
      noPhoto: 'لا توجد صورة',
      uploading: 'جارٍ الرفع…',
      replacePhoto: 'استبدال الصورة',
      uploadPhoto: 'رفع صورة',
      removePhoto: 'إزالة الصورة',
      photoHint: 'JPEG أو PNG أو WebP، بحجم أقصاه 5 ميغابايت.',
      allergens: 'مسببات الحساسية',
      tags: 'الوسوم',
      tagsHint: '(افصل بينها بفاصلة)',
      tagsPlaceholder: 'نباتي، حار، اختيار الشيف',
      available: 'متوفر',
      availableHint: 'ألغِ التحديد لإظهاره على أنه نفد.',
      modifierGroups: 'مجموعات الإضافات',
      addGroup: '+ إضافة مجموعة',
      noGroups: 'لا توجد خيارات. أضف مجموعة لخيارات مثل الحجم أو درجة الاستواء أو الإضافات.',
      groupName: 'اسم المجموعة',
      groupNamePlaceholder: 'مثال: الحجم',
      minPicks: 'الحد الأدنى للاختيار',
      maxPicks: 'الحد الأقصى للاختيار',
      moveGroupUp: 'نقل المجموعة للأعلى',
      moveGroupDown: 'نقل المجموعة للأسفل',
      removeGroup: 'إزالة المجموعة',
      pickRule: (min: number, max: number) =>
        `${min > 0 ? 'إجباري' : 'اختياري'}: يختار الضيف ${min === max ? max : min > 0 ? `${min}–${max}` : `حتى ${max}`}.`,
      option: 'الخيار',
      arabic: 'بالعربية',
      extraPriceHeader: '+ السعر',
      default: 'افتراضي',
      inStock: 'متوفر',
      optionName: 'اسم الخيار',
      arabicOptionName: 'اسم الخيار بالعربية',
      extraPrice: 'السعر الإضافي',
      selectedByDefault: 'محدد افتراضياً',
      removeOption: 'إزالة الخيار',
      addOption: '+ إضافة خيار',
      saveItem: 'حفظ الصنف',
      addItem: 'إضافة الصنف',
      imageTooLarge: 'يجب ألا يتجاوز حجم الصورة 5 ميغابايت.',
      enterItemName: 'أدخل اسم الصنف.',
      invalidPrice: (currency: string) => `أدخل سعراً صحيحاً بـ ${currency}، مثال: 25.00.`,
      tooManyTags: 'استخدم 8 وسوم كحد أقصى.',
      tagTooLong: 'يجب ألا يتجاوز طول الوسم 24 حرفاً.',
      aGroup: 'مجموعة إضافات',
      groupNeedsName: 'يجب أن يكون لكل مجموعة إضافات اسم.',
      groupNeedsOption: (group: string) => `تحتاج "${group}" إلى خيار واحد على الأقل.`,
      pickRange: (group: string) => `"${group}": يجب أن يكون الحد الأدنى للاختيار بين 0 والحد الأقصى، وأن يكون الحد الأقصى 1 على الأقل.`,
      maxOverOptions: (group: string) => `"${group}": لا يمكن أن يتجاوز الحد الأقصى للاختيار عدد الخيارات.`,
      tooManyDefaults: (group: string) => `"${group}": عدد الخيارات المحددة افتراضياً أكبر مما يُسمح للضيف باختياره.`,
      optionNeedsName: (group: string) => `يجب أن يكون لكل خيار في "${group}" اسم.`,
      optionInvalidPrice: (option: string) => `سعر "${option}" غير صحيح.`
    }
  }
};
