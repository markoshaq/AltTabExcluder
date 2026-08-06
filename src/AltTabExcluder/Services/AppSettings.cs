using System;
using System.IO;
using System.Text.Json;

namespace AltTabExcluder.Services;

/// <summary>
/// Persisted app settings stored at <c>%APPDATA%\AltTabExcluder\settings.json</c>.
/// Currently holds the global hotkey configuration; can be extended for future
/// settings without changing the storage format.
/// </summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly string DirPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AltTabExcluder");

    private static readonly string FilePath = Path.Combine(DirPath, "settings.json");

    /// <summary>Modifier flags (Win=0x0008, Alt=0x0001, Ctrl=0x0002, Shift=0x0004, NoRepeat=0x4000).</summary>
    public uint HotkeyModifiers { get; set; } = 0x0008 | 0x0001 | 0x4000; // Win+Alt+NoRepeat

    /// <summary>Virtual key code (e.g. 0x58 = 'X').</summary>
    public uint HotkeyKey { get; set; } = 0x58;

    /// <summary>HWNDs (as long values) that AltTabExcluder has excluded.
    /// Persisted so Quick Exclude can still identify our exclusions after
    /// restart. Stale entries (closed windows) are harmless.</summary>
    public System.Collections.Generic.List<long> ExcludedByUs { get; set; } = new();

    /// <summary>Human-readable label for the current hotkey.</summary>
    public string HotkeyLabel => FormatHotkey(HotkeyModifiers, HotkeyKey);

    /// <summary>Loads settings from disk, or returns defaults if no file exists.</summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings is not null)
                    return settings;
            }
        }
        catch { /* corrupt/inaccessible — use defaults */ }

        return new AppSettings();
    }

    /// <summary>Saves settings to disk atomically.</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DirPath);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(FilePath))
                File.Replace(tmp, FilePath, destinationBackupFileName: null);
            else
                File.Move(tmp, FilePath);
        }
        catch { /* best-effort persistence */ }
    }

    /// <summary>Formats a modifier+key combo as a human-readable string.</summary>
    public static string FormatHotkey(uint modifiers, uint key)
    {
        var parts = new System.Collections.Generic.List<string>();
        if ((modifiers & 0x0008) != 0) parts.Add("Win");
        if ((modifiers & 0x0002) != 0) parts.Add("Ctrl");
        if ((modifiers & 0x0004) != 0) parts.Add("Shift");
        if ((modifiers & 0x0001) != 0) parts.Add("Alt");

        string keyName = key switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x1B => "Esc",
            0x20 => "Space",
            >= 0x30 and <= 0x39 => ((char)key).ToString(),      // 0-9
            >= 0x41 and <= 0x5A => ((char)key).ToString(),      // A-Z
            >= 0x70 and <= 0x7B => $"F{key - 0x6F}",             // F1-F12
            0xBA => ";",
            0xBB => "+",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            _ => $"0x{key:X2}",
        };

        parts.Add(keyName);
        return string.Join("+", parts);
    }
}
