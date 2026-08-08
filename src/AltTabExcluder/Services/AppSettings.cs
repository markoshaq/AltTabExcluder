using System.Text.Json;
using System.Text.Json.Serialization;

namespace AltTabExcluder.Services;

/// <summary>
/// A persisted excluded-by-us HWND entry: the HWND value (as a long) and the
/// PID at the time of exclusion. The PID is used to detect HWND recycling.
/// </summary>
public sealed record ExcludedHwndEntry
{
    public long Hwnd { get; init; }
    public uint Pid { get; init; }
}

/// <summary>
/// Persisted app settings stored at <c>%APPDATA%\AltTabExcluder\settings.json</c>.
/// Holds the global hotkey configuration (modifiers, key, enabled state) and the
/// excluded-by-us HWND tracking set. Can be extended for future settings
/// without changing the storage format.
/// </summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new ExcludedByUsConverter() },
    };

    private static readonly string DefaultDirPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AltTabExcluder");

    private static readonly string DefaultFilePath = Path.Combine(DefaultDirPath, "settings.json");

    /// <summary>Modifier flags (see <see cref="Win32Constants"/> MOD_* values).</summary>
    public uint HotkeyModifiers { get; set; } = Win32Constants.DefaultHotkeyModifiers;

    /// <summary>Virtual key code (e.g. 0x58 = 'X').</summary>
    public uint HotkeyKey { get; set; } = Win32Constants.DefaultHotkeyKey;

    /// <summary>Whether the global hotkey was enabled when the app last exited.
    /// Persisted so the user's enable/disable choice survives restarts.</summary>
    public bool HotkeyEnabled { get; set; } = true;

    /// <summary>HWND+PID pairs that AltTabExcluder has excluded.
    /// Persisted so Quick Exclude can still identify our exclusions after
    /// restart. The PID is used to detect HWND recycling. Stale entries
    /// (closed windows) are pruned periodically.</summary>
    public List<ExcludedHwndEntry> ExcludedByUs { get; set; } = new();

    /// <summary>Human-readable label for the current hotkey.</summary>
    public string HotkeyLabel => FormatHotkey(HotkeyModifiers, HotkeyKey);

    /// <summary>Loads settings from the default path, or returns defaults if no file exists.</summary>
    public static AppSettings Load() => Load(DefaultFilePath);

    /// <summary>
    /// Loads settings from <paramref name="filePath"/>, or returns defaults if
    /// no file exists. Internal — used by tests to redirect to a temp path.
    /// </summary>
    internal static AppSettings Load(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings is not null)
                    return settings;
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning(ex, "Failed to load settings — using defaults");
        }

        return new AppSettings();
    }

    /// <summary>Saves settings to the default path atomically.</summary>
    public void Save() => Save(DefaultFilePath);

    /// <summary>
    /// Saves settings to <paramref name="filePath"/> atomically. Internal —
    /// used by tests to redirect to a temp path.
    /// </summary>
    internal void Save(string filePath)
    {
        try
        {
            string dirPath = Path.GetDirectoryName(filePath) ?? string.Empty;
            Directory.CreateDirectory(dirPath);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            var tmp = filePath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(filePath))
                File.Replace(tmp, filePath, destinationBackupFileName: null);
            else
                File.Move(tmp, filePath);
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning(ex, "Failed to save settings");
        }
    }

    /// <summary>Formats a modifier+key combo as a human-readable string.</summary>
    public static string FormatHotkey(uint modifiers, uint key)
    {
        // Strip NoRepeat — it's an internal flag, not something the user picks.
        modifiers &= ~Win32Constants.MOD_NOREPEAT;

        var parts = new List<string>(5);
        if ((modifiers & Win32Constants.MOD_WIN) != 0) parts.Add("Win");
        if ((modifiers & Win32Constants.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((modifiers & Win32Constants.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((modifiers & Win32Constants.MOD_ALT) != 0) parts.Add("Alt");

        parts.Add(KeyToString(key));
        return string.Join("+", parts);
    }

    /// <summary>Converts a virtual-key code to its display name.</summary>
    public static string KeyToString(uint key) => key switch
    {
        Win32Constants.VK_BACK => "Backspace",
        Win32Constants.VK_TAB => "Tab",
        Win32Constants.VK_RETURN => "Enter",
        Win32Constants.VK_ESCAPE => "Esc",
        Win32Constants.VK_SPACE => "Space",
        >= 0x30 and <= 0x39 => ((char)key).ToString(),      // 0-9
        >= 0x41 and <= 0x5A => ((char)key).ToString(),      // A-Z
        >= Win32Constants.VK_F1 and <= Win32Constants.VK_F12 => $"F{key - 0x6F}", // F1-F12
        Win32Constants.VK_OEM_1 => ";",
        Win32Constants.VK_OEM_PLUS => "+",
        Win32Constants.VK_OEM_COMMA => ",",
        Win32Constants.VK_OEM_MINUS => "-",
        Win32Constants.VK_OEM_PERIOD => ".",
        Win32Constants.VK_OEM_2 => "/",
        Win32Constants.VK_OEM_3 => "`",
        Win32Constants.VK_OEM_4 => "[",
        Win32Constants.VK_OEM_5 => "\\",
        Win32Constants.VK_OEM_6 => "]",
        _ => $"0x{key:X2}",
    };
}

/// <summary>
/// Custom JSON converter for <see cref="AppSettings.ExcludedByUs"/> that
/// handles backward compatibility with the old flat-array format
/// (<c>[12345, 67890]</c>) by treating bare numbers as HWNDs with PID=0
/// (unknown). The new format uses objects (<c>[{"hwnd":12345,"pid":5678}]</c>).
/// </summary>
file sealed class ExcludedByUsConverter : JsonConverter<List<ExcludedHwndEntry>>
{
    public override List<ExcludedHwndEntry>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            return new List<ExcludedHwndEntry>();

        var list = new List<ExcludedHwndEntry>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                // Old format: bare number = HWND, PID unknown (0).
                long hwnd = reader.GetInt64();
                list.Add(new ExcludedHwndEntry { Hwnd = hwnd, Pid = 0 });
            }
            else if (reader.TokenType == JsonTokenType.StartObject)
            {
                // New format: object with "hwnd" and "pid" properties.
                using var doc = JsonDocument.ParseValue(ref reader);
                var root = doc.RootElement;
                long hwnd = root.TryGetProperty("hwnd", out var hwndProp) ? hwndProp.GetInt64() : 0;
                uint pid = root.TryGetProperty("pid", out var pidProp) ? pidProp.GetUInt32() : 0;
                list.Add(new ExcludedHwndEntry { Hwnd = hwnd, Pid = pid });
            }
            // Skip any other token types gracefully.
        }
        return list;
    }

    public override void Write(Utf8JsonWriter writer, List<ExcludedHwndEntry> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var entry in value)
        {
            writer.WriteStartObject();
            writer.WriteNumber("hwnd", entry.Hwnd);
            writer.WriteNumber("pid", entry.Pid);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }
}
