using System.Globalization;

namespace ScrollKeeper.Services.Addons;

/// <summary>
/// One entry of a manifest's <c>## DependsOn:</c> or <c>## OptionalDependsOn:</c> line,
/// e.g. <c>LibAddonMenu-2.0&gt;=32</c>.
/// </summary>
/// <param name="Name">Folder name of the required addon or library.</param>
/// <param name="MinAddOnVersion">Lowest accepted <c>## AddOnVersion</c>, or <c>null</c> for any version.</param>
public sealed record AddonDependency(string Name, int? MinAddOnVersion)
{
    /// <summary>
    /// Parses a space-separated dependency list and appends its entries to <paramref name="target"/>.
    /// A name listed again (e.g. in both <c>DependsOn</c> and <c>PCDependsOn</c>) keeps the highest minimum version.
    /// </summary>
    public static void ParseList(string value, List<AddonDependency> target)
    {
        foreach (var token in AddonManifest.StripFormatting(value).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryParse(token) is not { } dependency)
                continue;

            var index = target.FindIndex(d => string.Equals(d.Name, dependency.Name, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                target.Add(dependency);
            else if (dependency.MinAddOnVersion > (target[index].MinAddOnVersion ?? int.MinValue))
                target[index] = target[index] with { MinAddOnVersion = dependency.MinAddOnVersion };
        }
    }

    /// <summary>Parses <c>Name</c> or <c>Name&gt;=Version</c>; <c>null</c> for an empty name.</summary>
    public static AddonDependency? TryParse(string token)
    {
        var text = token.Trim();
        var separator = text.IndexOf(">=", StringComparison.Ordinal);
        var name = (separator < 0 ? text : text[..separator]).Trim();
        if (name.Length == 0)
            return null;

        int? minVersion = null;
        if (separator >= 0
            && int.TryParse(text[(separator + 2)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var version))
        {
            minVersion = version;
        }

        return new AddonDependency(name, minVersion);
    }

    public override string ToString() => MinAddOnVersion is { } v ? $"{Name}>={v}" : Name;
}
