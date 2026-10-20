using DineFlow.Domain.Common;

namespace DineFlow.Domain.Menus;

/// <summary>
/// One of a venue's menus, e.g. "Breakfast" or "All day" (blueprint P1-MENU-04). Guests can
/// browse any published menu but only order from one that is available right now.
/// </summary>
public class Menu : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid VenueId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? NameAr { get; private set; }
    public int SortOrder { get; private set; }
    /// <summary>Unpublished menus are only visible in the merchant dashboard.</summary>
    public bool IsPublished { get; private set; }

    public MenuDays Days { get; private set; } = MenuDays.Everyday;
    /// <summary>Start of the daily window in venue local time; null with <see cref="EndTime"/> means all day.</summary>
    public TimeOnly? StartTime { get; private set; }
    /// <summary>End of the daily window (exclusive). Earlier than the start means it runs past midnight.</summary>
    public TimeOnly? EndTime { get; private set; }

    private Menu() { }

    public Menu(Guid tenantId, Guid venueId, string name, string? nameAr, int sortOrder)
    {
        TenantId = tenantId;
        VenueId = venueId;
        SortOrder = sortOrder;
        Rename(name, nameAr);
    }

    public void Rename(string name, string? nameAr)
    {
        Name = Text.Required(name, 80, "Menu name");
        NameAr = Text.Optional(nameAr, 80);
    }

    public void SetPublished(bool published) => IsPublished = published;

    public void SetSortOrder(int sortOrder) => SortOrder = sortOrder;

    public void SetSchedule(MenuDays days, TimeOnly? start, TimeOnly? end)
    {
        if (days == MenuDays.None) throw new DomainException("Pick at least one day for the menu.");
        if (start.HasValue != end.HasValue) throw new DomainException("Set both a start and an end time, or neither for all day.");
        if (start.HasValue && start == end) throw new DomainException("Start and end time can't be the same.");

        Days = days;
        StartTime = start;
        EndTime = end;
    }

    /// <summary>Whether guests can order from this menu at the given venue-local time.</summary>
    public bool IsAvailableAt(DateTime local)
    {
        if (!IsPublished) return false;

        var time = TimeOnly.FromDateTime(local);
        if (StartTime is not { } start || EndTime is not { } end) return RunsOn(local.DayOfWeek);

        if (start < end) return RunsOn(local.DayOfWeek) && time >= start && time < end;

        // Overnight window, e.g. 22:00-02:00: the part after midnight belongs to the previous day.
        return time >= start
            ? RunsOn(local.DayOfWeek)
            : time < end && RunsOn(local.AddDays(-1).DayOfWeek);
    }

    private bool RunsOn(DayOfWeek day) => Days.HasFlag(MenuDaysExtensions.For(day));
}

[Flags]
public enum MenuDays
{
    None = 0,
    Sunday = 1,
    Monday = 2,
    Tuesday = 4,
    Wednesday = 8,
    Thursday = 16,
    Friday = 32,
    Saturday = 64,
    Everyday = Sunday | Monday | Tuesday | Wednesday | Thursday | Friday | Saturday
}

public static class MenuDaysExtensions
{
    public static MenuDays For(DayOfWeek day) => (MenuDays)(1 << (int)day);

    public static IReadOnlyList<DayOfWeek> ToDays(this MenuDays days) =>
        Enum.GetValues<DayOfWeek>().Where(d => days.HasFlag(For(d))).ToList();

    public static MenuDays FromDays(IEnumerable<DayOfWeek> days) =>
        days.Aggregate(MenuDays.None, (all, day) => all | For(day));
}
