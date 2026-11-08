namespace ScrollKeeper.Services.Settings;

/// <summary>User preferences persisted between application runs.</summary>
public sealed record AppSettings
{
    /// <summary>
    /// The game's AddOns folder, e.g. <c>Documents\Elder Scrolls Online\live\AddOns</c>.
    /// <c>null</c> uses the folder found by <see cref="EsoPaths.FindAddonsFolder"/>.
    /// </summary>
    public string? AddonsFolderPath { get; init; }

    /// <summary>
    /// When enabled, the latest GitHub release is checked on every start and, if newer,
    /// downloaded, installed and the app restarted automatically.
    /// </summary>
    public bool AutoUpdate { get; init; } = true;

    /// <summary>Longest allowed delay for <see cref="RestartDelaySeconds"/>.</summary>
    public const int MaxRestartDelaySeconds = 300;

    /// <summary>
    /// Countdown (in seconds) shown before the app closes to install a downloaded update.
    /// 0 restarts immediately.
    /// </summary>
    public int RestartDelaySeconds { get; init; } = 5;
}
