using DineFlow.Domain.Common;
using DineFlow.Domain.Menus;

namespace DineFlow.Test.Domain;

public class MenuScheduleTests
{
    // 2026-10-19 is a Monday.
    private static DateTime Monday(int hour, int minute = 0) => new(2026, 10, 19, hour, minute, 0);

    private static Menu Create(MenuDays days, TimeOnly? start = null, TimeOnly? end = null, bool published = true)
    {
        var menu = new Menu(Guid.NewGuid(), Guid.NewGuid(), "Menu", null, 0);
        menu.SetSchedule(days, start, end);
        menu.SetPublished(published);
        return menu;
    }

    [Fact]
    public void All_day_every_day_is_always_available()
    {
        var menu = Create(MenuDays.Everyday);

        Assert.True(menu.IsAvailableAt(Monday(0)));
        Assert.True(menu.IsAvailableAt(Monday(23, 59)));
    }

    [Fact]
    public void Breakfast_window_includes_start_and_excludes_end()
    {
        var breakfast = Create(MenuDays.Everyday, new TimeOnly(7, 0), new TimeOnly(11, 30));

        Assert.False(breakfast.IsAvailableAt(Monday(6, 59)));
        Assert.True(breakfast.IsAvailableAt(Monday(7, 0)));
        Assert.True(breakfast.IsAvailableAt(Monday(11, 29)));
        Assert.False(breakfast.IsAvailableAt(Monday(11, 30)));
    }

    [Fact]
    public void Weekend_menu_is_not_available_on_a_weekday()
    {
        var weekend = Create(MenuDays.Friday | MenuDays.Saturday);

        Assert.False(weekend.IsAvailableAt(Monday(12)));
        Assert.True(weekend.IsAvailableAt(new DateTime(2026, 10, 23, 12, 0, 0))); // Friday
    }

    [Fact]
    public void Overnight_window_after_midnight_counts_as_the_previous_day()
    {
        // Late menu on Sundays only, 22:00-02:00.
        var late = Create(MenuDays.Sunday, new TimeOnly(22, 0), new TimeOnly(2, 0));

        Assert.True(late.IsAvailableAt(new DateTime(2026, 10, 18, 23, 0, 0))); // Sunday 23:00
        Assert.True(late.IsAvailableAt(Monday(1, 30)));                        // still Sunday's window
        Assert.False(late.IsAvailableAt(Monday(2, 0)));
        Assert.False(late.IsAvailableAt(Monday(23, 0)));                       // Monday night isn't scheduled
    }

    [Fact]
    public void Unpublished_menu_is_never_available()
    {
        Assert.False(Create(MenuDays.Everyday, published: false).IsAvailableAt(Monday(12)));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Start_and_end_must_be_set_together(bool hasStart, bool hasEnd)
    {
        var menu = new Menu(Guid.NewGuid(), Guid.NewGuid(), "Menu", null, 0);

        Assert.Throws<DomainException>(() => menu.SetSchedule(MenuDays.Everyday,
            hasStart ? new TimeOnly(7, 0) : null, hasEnd ? new TimeOnly(11, 0) : null));
    }

    [Fact]
    public void A_menu_needs_at_least_one_day()
    {
        var menu = new Menu(Guid.NewGuid(), Guid.NewGuid(), "Menu", null, 0);

        Assert.Throws<DomainException>(() => menu.SetSchedule(MenuDays.None, null, null));
    }
}

public class MenuItemTests
{
    private static MenuItem Create()
    {
        var item = new MenuItem(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0);
        item.Update(item.CategoryId, "Burger", null, null, null, 5500, null, Allergen.None, []);
        return item;
    }

    private static ModifierOptionSpec Option(string name, long price = 0, Guid? id = null, bool isDefault = false) =>
        new(id, name, null, price, isDefault, true);

    [Fact]
    public void Updating_modifiers_keeps_ids_of_existing_groups_and_options()
    {
        var item = Create();
        item.SetModifierGroups([new(null, "Size", null, 1, 1, [Option("Small"), Option("Large", 400)])]);
        var size = item.ModifierGroups.Single();
        var large = size.Options[1];

        item.SetModifierGroups([
            new(null, "Extras", null, 0, 2, [Option("Cheese", 300), Option("Bacon", 600)]),
            new(size.Id, "Size", "الحجم", 1, 1, [Option("Large", 500, large.Id), Option("Medium", 200)])
        ]);

        Assert.Equal(["Extras", "Size"], item.ModifierGroups.Select(g => g.Name));
        var updatedSize = item.ModifierGroups[1];
        Assert.Same(size, updatedSize);
        Assert.Equal("الحجم", updatedSize.NameAr);
        Assert.Equal(["Large", "Medium"], updatedSize.Options.Select(o => o.Name));
        Assert.Same(large, updatedSize.Options[0]);
        Assert.Equal(500, updatedSize.Options[0].PriceDeltaMinor);
    }

    [Fact]
    public void Missing_groups_are_removed()
    {
        var item = Create();
        item.SetModifierGroups([new(null, "Size", null, 1, 1, [Option("Small")])]);

        item.SetModifierGroups([]);

        Assert.Empty(item.ModifierGroups);
    }

    [Theory]
    [InlineData(2, 1)] // min above max
    [InlineData(0, 3)] // max above number of options
    [InlineData(0, 0)] // max must be at least 1
    public void Invalid_pick_limits_are_rejected(int min, int max)
    {
        var item = Create();

        Assert.Throws<DomainException>(() =>
            item.SetModifierGroups([new(null, "Size", null, min, max, [Option("Small"), Option("Large")])]));
    }

    [Fact]
    public void More_defaults_than_allowed_picks_is_rejected()
    {
        var item = Create();

        Assert.Throws<DomainException>(() => item.SetModifierGroups([
            new(null, "Size", null, 1, 1, [Option("Small", isDefault: true), Option("Large", isDefault: true)])
        ]));
    }

    [Fact]
    public void A_group_needs_options_and_unknown_ids_are_rejected()
    {
        var item = Create();

        Assert.Throws<DomainException>(() => item.SetModifierGroups([new(null, "Empty", null, 0, 1, [])]));
        Assert.Throws<DomainException>(() => item.SetModifierGroups([new(Guid.NewGuid(), "Size", null, 0, 1, [Option("Small")])]));
    }

    [Fact]
    public void Tags_are_trimmed_and_deduplicated_ignoring_case()
    {
        var item = Create();

        item.Update(item.CategoryId, "Burger", null, null, null, 5500, null, Allergen.None, [" Spicy ", "spicy", "", "Chef's pick"]);

        Assert.Equal(["Spicy", "Chef's pick"], item.Tags);
    }

    [Fact]
    public void Negative_prices_are_rejected()
    {
        var item = Create();

        Assert.Throws<DomainException>(() => item.Update(item.CategoryId, "Burger", null, null, null, -1, null, Allergen.None, []));
        Assert.Throws<DomainException>(() => item.SetModifierGroups([new(null, "Size", null, 0, 1, [Option("Small", -100)])]));
    }
}
