using System.Collections;
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
    /// <summary>Typed comparer for <c>ListCollectionView.CustomSort</c> (much faster than SortDescriptions).</summary>
    public IComparer Comparer => Sort switch
    {
        BrowseSort.RecentlyUpdated => new AddonComparer((a, b) => Nullable.Compare(b.Updated, a.Updated)),
        BrowseSort.Name => new AddonComparer((_, _) => 0),
        _ => new AddonComparer((a, b) => b.Downloads.CompareTo(a.Downloads))
    };

    public override string ToString() => DisplayName;
}

/// <summary>Sorts by the given key, then by name.</summary>
internal sealed class AddonComparer(Comparison<AddonItemViewModel> primary) : IComparer
{
    public int Compare(object? x, object? y)
    {
        if (x is not AddonItemViewModel a || y is not AddonItemViewModel b)
            return 0;

        var result = primary(a, b);
        return result != 0 ? result : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
    }
}
