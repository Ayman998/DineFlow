using DineFlow.Domain.Menus;
using DineFlow.Domain.Venues;

namespace DineFlow.Infrastructure.Persistence;

/// <summary>A small bilingual demo menu so the guest e-menu has something to show in development.</summary>
internal static class DemoMenuSeed
{
    public static void Add(AppDbContext db, Venue venue)
    {
        var allDay = AddMenu(db, venue, "All day", "طوال اليوم", 0, null, null);
        var breakfast = AddMenu(db, venue, "Breakfast", "الفطور", 1, new TimeOnly(7, 0), new TimeOnly(11, 30));

        var starters = AddCategory(db, allDay, null, "Starters", "المقبلات", 0);
        Item(db, starters, 0, "Hummus", "حمص", 2800, "Chickpeas, tahini, olive oil and warm pita.", "حمص بالطحينة وزيت الزيتون مع خبز دافئ.",
            Allergen.Sesame | Allergen.Gluten, ["Vegan"]);
        Item(db, starters, 1, "Fattoush", "فتوش", 3200, "Crisp vegetables, sumac and toasted bread.", "خضار طازجة وسماق وخبز محمص.",
            Allergen.Gluten, ["Vegan"]);
        Item(db, starters, 2, "Lentil soup", "شوربة عدس", 2400, "Red lentils, cumin and lemon.", "عدس أحمر وكمون وليمون.",
            Allergen.Celery, ["Vegetarian"]).SetAvailable(false);

        var mains = AddCategory(db, allDay, null, "Mains", "الأطباق الرئيسية", 1);
        var grills = AddCategory(db, allDay, mains.Id, "Grills", "المشاوي", 0);
        Item(db, grills, 0, "Mixed grill", "مشاوي مشكلة", 9500, "Shish tawook, kofta and lamb kebab with rice.", "شيش طاووق وكفتة وكباب لحم مع الأرز.",
            Allergen.Milk, ["Chef's pick"],
            Group("Side", "الطبق الجانبي", 1, 1, Option("Rice", "أرز", 0, isDefault: true), Option("Fries", "بطاطس مقلية", 0), Option("Salad", "سلطة", 500)));
        Item(db, grills, 1, "Chicken shawarma plate", "صحن شاورما دجاج", 4800, "Garlic sauce, pickles and fries.", "ثومية ومخللات وبطاطس.",
            Allergen.Gluten | Allergen.Sesame | Allergen.Milk, ["Spicy"],
            Group("Spice level", "درجة الحرارة", 1, 1, Option("Mild", "خفيف", 0), Option("Medium", "متوسط", 0, isDefault: true), Option("Hot", "حار", 0)));

        var burgers = AddCategory(db, allDay, mains.Id, "Burgers", "البرجر", 1);
        Item(db, burgers, 0, "Classic beef burger", "برجر لحم كلاسيك", 5500, "Beef patty, cheddar, pickles and house sauce.", "لحم بقري وجبنة شيدر ومخلل وصلصة خاصة.",
            Allergen.Gluten | Allergen.Milk | Allergen.Eggs | Allergen.Sesame, [],
            Group("Doneness", "درجة الطهي", 1, 1, Option("Medium", "متوسط", 0), Option("Medium well", "متوسط إلى جيد", 0), Option("Well done", "مطهو جيداً", 0)),
            Group("Add extras", "إضافات", 0, 3, Option("Extra cheese", "جبنة إضافية", 400), Option("Beef bacon", "لحم مقدد", 600),
                Option("Avocado", "أفوكادو", 500), Option("Jalapeños", "هالابينو", 300)));
        Item(db, burgers, 1, "Halloumi burger", "برجر حلوم", 4900, "Grilled halloumi, rocket and tomato jam.", "حلوم مشوي وجرجير ومربى طماطم.",
            Allergen.Gluten | Allergen.Milk, ["Vegetarian"],
            Group("Add extras", "إضافات", 0, 2, Option("Avocado", "أفوكادو", 500), Option("Jalapeños", "هالابينو", 300)));

        var desserts = AddCategory(db, allDay, null, "Desserts", "الحلويات", 2);
        Item(db, desserts, 0, "Kunafa", "كنافة", 3500, "Warm cheese kunafa with pistachio.", "كنافة بالجبنة مع الفستق.",
            Allergen.Milk | Allergen.Gluten | Allergen.TreeNuts, ["Chef's pick"]);
        Item(db, desserts, 1, "Umm Ali", "أم علي", 3000, "Baked pastry pudding with nuts.", "حلوى المعجنات بالحليب والمكسرات.",
            Allergen.Milk | Allergen.Gluten | Allergen.TreeNuts, []);

        var drinks = AddCategory(db, allDay, null, "Drinks", "المشروبات", 3);
        var hot = AddCategory(db, allDay, drinks.Id, "Hot drinks", "مشروبات ساخنة", 0);
        Item(db, hot, 0, "Arabic coffee", "قهوة عربية", 1500, "With cardamom, served with dates.", "بالهيل مع التمر.", Allergen.None, []);
        Item(db, hot, 1, "Flat white", "فلات وايت", 2200, "Double shot with steamed milk.", "شوتين إسبريسو مع حليب.", Allergen.Milk, [],
            Group("Size", "الحجم", 1, 1, Option("Regular", "عادي", 0, isDefault: true), Option("Large", "كبير", 400)),
            Group("Milk", "الحليب", 0, 1, Option("Oat milk", "حليب الشوفان", 300), Option("Almond milk", "حليب اللوز", 300)));
        var cold = AddCategory(db, allDay, drinks.Id, "Cold drinks", "مشروبات باردة", 1);
        Item(db, cold, 0, "Fresh lemon mint", "ليمون بالنعناع", 2200, "Blended lemon and fresh mint.", "ليمون ونعناع طازج.", Allergen.None, ["Vegan"]);
        Item(db, cold, 1, "Mineral water", "مياه معدنية", 800, null, null, Allergen.None, []);

        var morning = AddCategory(db, breakfast, null, "Breakfast plates", "أطباق الفطور", 0);
        Item(db, morning, 0, "Shakshuka", "شكشوكة", 3800, "Eggs poached in spiced tomato sauce.", "بيض مطهو في صلصة الطماطم المتبلة.",
            Allergen.Eggs, ["Vegetarian"]);
        Item(db, morning, 1, "Labneh & za'atar manakish", "مناقيش لبنة وزعتر", 2600, null, null,
            Allergen.Gluten | Allergen.Milk | Allergen.Sesame, ["Vegetarian"]);
        Item(db, morning, 2, "Foul medames", "فول مدمس", 2800, "Fava beans, cumin and olive oil.", "فول بالكمون وزيت الزيتون.", Allergen.None, ["Vegan"]);
        var morningDrinks = AddCategory(db, breakfast, null, "Breakfast drinks", "مشروبات الفطور", 1);
        Item(db, morningDrinks, 0, "Karak chai", "شاي كرك", 1200, "Spiced milk tea.", "شاي بالحليب والبهارات.", Allergen.Milk, []);
    }

    private static Menu AddMenu(AppDbContext db, Venue venue, string name, string nameAr, int order, TimeOnly? start, TimeOnly? end)
    {
        var menu = new Menu(venue.TenantId, venue.Id, name, nameAr, order);
        menu.SetSchedule(MenuDays.Everyday, start, end);
        menu.SetPublished(true);
        db.Menus.Add(menu);
        return menu;
    }

    private static MenuCategory AddCategory(AppDbContext db, Menu menu, Guid? parentId, string name, string nameAr, int order)
    {
        var category = new MenuCategory(menu.TenantId, menu.VenueId, menu.Id, parentId, name, nameAr, order);
        db.MenuCategories.Add(category);
        return category;
    }

    private static MenuItem Item(AppDbContext db, MenuCategory category, int order, string name, string nameAr, long priceMinor,
        string? description, string? descriptionAr, Allergen allergens, string[] tags, params ModifierGroupSpec[] groups)
    {
        var item = new MenuItem(category.TenantId, category.VenueId, category.Id, order);
        item.Update(category.Id, name, nameAr, description, descriptionAr, priceMinor, null, allergens, tags);
        item.SetModifierGroups(groups);
        db.MenuItems.Add(item);
        return item;
    }

    private static ModifierGroupSpec Group(string name, string nameAr, int min, int max, params ModifierOptionSpec[] options) =>
        new(null, name, nameAr, min, max, options);

    private static ModifierOptionSpec Option(string name, string nameAr, long priceDeltaMinor, bool isDefault = false) =>
        new(null, name, nameAr, priceDeltaMinor, isDefault, IsAvailable: true);
}
