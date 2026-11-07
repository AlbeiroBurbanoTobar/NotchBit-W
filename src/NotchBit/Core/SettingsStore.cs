using System.Text.Json;

namespace NotchBit.Core;

/// <summary>User settings, kept as JSON in %LOCALAPPDATA%\NotchBit\settings.json.</summary>
public sealed class SettingsStore
{
    private sealed class Data
    {
        public List<string> DisabledModules { get; set; } = [];
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private Data _data;

    public SettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NotchBit", "settings.json");
        _data = Load();
    }

    public IReadOnlyCollection<string> DisabledModules => _data.DisabledModules;

    public void SetDisabledModules(IEnumerable<string> ids)
    {
        _data.DisabledModules = ids.Distinct().Order().ToList();
        Save();
    }

    private Data Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<Data>(File.ReadAllText(_path)) ?? new Data();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // A missing or damaged settings file just means defaults.
        }
        return new Data();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_data, JsonOptions));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Losing a setting is better than crashing the island.
        }
    }
}
