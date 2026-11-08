using System.IO;
using System.Windows;
using ScrollKeeper.Services.Addons;

namespace ScrollKeeper.Presentation.Views.Addons;

/// <summary>Lists what removing an addon deletes (folders, unused libraries, settings) and asks for confirmation.</summary>
public partial class ConfirmRemoveWindow : Window
{
    public ConfirmRemoveWindow(string addonName, RemovalPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        InitializeComponent();
        Heading = $"Remove {addonName}?";
        Sections = BuildSections(plan);
        DataContext = this;
    }

    public string Heading { get; }

    public IReadOnlyList<RemovalSection> Sections { get; }

    private static List<RemovalSection> BuildSections(RemovalPlan plan)
    {
        var sections = new List<RemovalSection>
        {
            new("Addon folders", plan.AddonFolders, IsWarning: false)
        };

        if (plan.UnusedDependencies.Count > 0)
            sections.Add(new("Libraries no other addon uses", plan.UnusedDependencies, IsWarning: false));

        if (plan.SavedVariablesFiles.Count > 0)
            sections.Add(new("Saved settings", plan.SavedVariablesFiles.Select(Path.GetFileName).OfType<string>().ToList(), IsWarning: false));

        if (plan.KeptDependencies.Count > 0)
            sections.Add(new("Kept, other addons still use them", plan.KeptDependencies, IsWarning: false));

        if (plan.RequiredBy.Count > 0)
            sections.Add(new("These addons require it and will stop working", plan.RequiredBy, IsWarning: true));

        return sections;
    }

    private void OnRemove(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}

/// <summary>One group of the confirmation list.</summary>
public sealed record RemovalSection(string Header, IReadOnlyList<string> Items, bool IsWarning);
