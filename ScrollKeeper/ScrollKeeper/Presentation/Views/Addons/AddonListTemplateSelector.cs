using Avalonia.Controls;
using Avalonia.Controls.Templates;
using ScrollKeeper.Presentation.ViewModels.Addons;

namespace ScrollKeeper.Presentation.Views.Addons;

/// <summary>
/// Template of the grouped Installed list, whose rows are <see cref="AddonGroup"/> headers and
/// <see cref="AddonItemViewModel"/> addons (Avalonia lists have no built-in grouping).
/// Not a recycling template: a recycled row may switch between a header and an addon.
/// </summary>
public sealed class AddonListTemplateSelector : IDataTemplate
{
    public IDataTemplate? GroupTemplate { get; set; }

    public IDataTemplate? ItemTemplate { get; set; }

    public Control? Build(object? param) => param switch
    {
        AddonGroup => GroupTemplate?.Build(param),
        AddonItemViewModel => ItemTemplate?.Build(param),
        _ => null
    };

    public bool Match(object? data) => data is AddonGroup or AddonItemViewModel;
}
