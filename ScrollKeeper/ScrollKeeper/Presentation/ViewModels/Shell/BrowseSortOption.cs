using ScrollKeeper.Presentation.ViewModels.Addons;

namespace ScrollKeeper.Presentation.ViewModels.Shell;

public enum BrowseSort
{
    Popular,
    RecentlyUpdated,
    Name
}

/// <summary>One choice of the Browse tab's sort selector.</summary>
public sealed record BrowseSortOption(BrowseSort Sort, string DisplayName)
{
    /// <summary>Sort order of the Browse list.</summary>
    public IComparer<AddonItemViewModel> Comparer => Sort switch
    {
        BrowseSort.RecentlyUpdated => new AddonComparer((a, b) => Nullable.Compare(b.Updated, a.Updated)),
        BrowseSort.Name => new AddonComparer((_, _) => 0),
        _ => new AddonComparer((a, b) => b.Downloads.CompareTo(a.Downloads))
    };

    public override string ToString() => DisplayName;
}

/// <summary>Sorts by the given key, then by name.</summary>
internal sealed class AddonComparer(Comparison<AddonItemViewModel> primary) : IComparer<AddonItemViewModel>
{
    public int Compare(AddonItemViewModel? a, AddonItemViewModel? b)
    {
        if (a is null || b is null)
            return a is null ? (b is null ? 0 : -1) : 1;

        var result = primary(a, b);
        return result != 0 ? result : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
    }
}
