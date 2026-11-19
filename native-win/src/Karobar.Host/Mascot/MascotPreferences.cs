using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Karobar.Host.Mascot;

public enum MascotTrick
{
    None,
    Rubiks,
    Headphones,
    Backflip,
    Dance,
    Juggle,
    Shades,
    Charge,
    Scan,
    Hoverboard,
    Walk,
    Sleep,
    Wake,
    Spy
}

public sealed class MascotPreferences
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("skin")]
    public string Skin { get; set; } = "cube"; // cube | pixel | bot

    [JsonPropertyName("roaming_enabled")]
    public bool RoamingEnabled { get; set; } = true;

    [JsonPropertyName("language")]
    public string Language { get; set; } = "en"; // en | ur

    [JsonPropertyName("drawer_wide")]
    public bool DrawerWide { get; set; }

    public MascotPreferences Clone() => new()
    {
        Enabled = Enabled,
        Skin = NormalizeSkin(Skin),
        RoamingEnabled = RoamingEnabled,
        Language = Language == "ur" ? "ur" : "en",
        DrawerWide = DrawerWide
    };

    public static string NormalizeSkin(string? raw)
    {
        var s = (raw ?? "cube").Trim().ToLowerInvariant();
        return s is "pixel" or "bot" ? s : "cube";
    }

    public static string NextSkin(string? current) => NormalizeSkin(current) switch
    {
        "cube" => "pixel",
        "pixel" => "bot",
        _ => "cube"
    };
}

public sealed class MascotPreferencesStore
{
    private readonly string _prefsPath;
    private readonly object _lock = new();
    private MascotPreferences _current;

    public event Action<MascotPreferences>? PreferencesChanged;

    public MascotPreferencesStore(string? userDataDirOverride = null)
    {
        var baseDir = !string.IsNullOrWhiteSpace(userDataDirOverride)
            ? userDataDirOverride
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                NetworkConfigManager.AppDataFolderName);
        _prefsPath = Path.Combine(baseDir, "mascot-prefs.json");
        _current = LoadFromDisk();
    }

    public MascotPreferences Current
    {
        get
        {
            lock (_lock) return _current.Clone();
        }
    }

    public MascotPreferences Update(Action<MascotPreferences> mutator)
    {
        MascotPreferences snapshot;
        lock (_lock)
        {
            mutator(_current);
            _current.Skin = MascotPreferences.NormalizeSkin(_current.Skin);
            _current.Language = _current.Language == "ur" ? "ur" : "en";
            snapshot = _current.Clone();
            SaveToDiskNoLock(snapshot);
        }
        try { PreferencesChanged?.Invoke(snapshot); } catch { }
        return snapshot;
    }

    private MascotPreferences LoadFromDisk()
    {
        try
        {
            if (File.Exists(_prefsPath))
            {
                var parsed = JsonSerializer.Deserialize<MascotPreferences>(File.ReadAllText(_prefsPath, Encoding.UTF8));
                if (parsed != null)
                {
                    parsed.Skin = MascotPreferences.NormalizeSkin(parsed.Skin);
                    parsed.Language = parsed.Language == "ur" ? "ur" : "en";
                    return parsed;
                }
            }
        }
        catch { }
        return new MascotPreferences();
    }

    private void SaveToDiskNoLock(MascotPreferences prefs)
    {
        try
        {
            var dir = Path.GetDirectoryName(_prefsPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = _prefsPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(prefs, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.Move(tmp, _prefsPath, overwrite: true);
        }
        catch { }
    }
}
