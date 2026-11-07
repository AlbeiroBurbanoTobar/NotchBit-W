namespace NotchBit.Core;

/// <summary>Every available module, and which ones the user has turned on.</summary>
public sealed class ModuleRegistry
{
    private readonly SettingsStore _settings;
    private readonly HashSet<string> _disabled;

    public ModuleRegistry(IEnumerable<IIslandModule> modules, SettingsStore settings)
    {
        AllModules = modules.ToList();
        _settings = settings;
        _disabled = [.. settings.DisabledModules];

        foreach (IIslandModule module in AllModules)
            module.CompactActivityChanged += () => CompactActivityChanged?.Invoke();
    }

    /// <summary>Raised on the UI thread when any module starts or stops having something to show beside the resting island.</summary>
    public event Action? CompactActivityChanged;

    public IReadOnlyList<IIslandModule> AllModules { get; }

    public IReadOnlyList<IIslandModule> EnabledModules =>
        AllModules.Where(m => !_disabled.Contains(m.Id)).ToList();

    /// <summary>Raised on the UI thread after a module is turned on or off.</summary>
    public event Action? Changed;

    public bool IsEnabled(string id) => !_disabled.Contains(id);

    public void SetEnabled(string id, bool enabled)
    {
        bool changed = enabled ? _disabled.Remove(id) : _disabled.Add(id);
        if (!changed) return;

        _settings.SetDisabledModules(_disabled);
        Changed?.Invoke();
    }
}
