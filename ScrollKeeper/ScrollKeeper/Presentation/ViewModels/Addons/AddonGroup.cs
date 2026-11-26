namespace ScrollKeeper.Presentation.ViewModels.Addons;

/// <summary>
/// A section of the Installed list: addons or libraries. The group object itself is the header row of the list
/// (see <c>MainViewModel.InstalledRows</c>), so the header binds to <c>IsExpanded</c> and
/// the collapsed state survives rescans, searches and the group disappearing while it is empty.
/// </summary>
public sealed partial class AddonGroup : ViewModelBase
{
    public static readonly AddonGroup Addons = new(0, "Addons");

    public static readonly AddonGroup Libraries = new(1, "Libraries");

    private AddonGroup(int order, string title)
    {
        Order = order;
        Title = title;
    }

    /// <summary>Position of the group in the list: addons first, then libraries.</summary>
    public int Order { get; }

    public string Title { get; }

    [ObservableProperty]
    private bool _isExpanded = true;

    /// <summary>Addons of the group that match the current search (shown next to the title).</summary>
    [ObservableProperty]
    private int _itemCount;

    public override string ToString() => Title;
}
