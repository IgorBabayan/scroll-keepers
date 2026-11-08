namespace ScrollKeeper.Services.Settings;

public interface ISettingsService
{
    /// <summary>Last saved (or loaded) settings.</summary>
    AppSettings Current { get; }

    /// <summary>
    /// AddOns folder in use: the saved one, otherwise the detected default.
    /// May point to a folder that does not exist.
    /// </summary>
    string AddonsFolder { get; }

    /// <summary>Raised after <see cref="Save"/> succeeds.</summary>
    event EventHandler<AppSettings>? SettingsChanged;

    void Save(AppSettings settings);
}
