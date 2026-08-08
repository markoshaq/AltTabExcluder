using AltTabExcluder.Services;

namespace AltTabExcluder.Tests;

/// <summary>
/// Tests for <see cref="AppSettings"/> hotkey formatting and key-name lookup.
/// These are pure string-formatting tests — no file I/O or Win32.
/// </summary>
public class AppSettingsFormatHotkeyTests
{
    [Fact]
    public void FormatHotkey_DefaultCombo_IsWinAltX()
    {
        string label = AppSettings.FormatHotkey(
            Win32Constants.MOD_WIN | Win32Constants.MOD_ALT,
            Win32Constants.DefaultHotkeyKey);

        Assert.Equal("Win+Alt+X", label);
    }

    [Fact]
    public void FormatHotkey_StripsNoRepeatFlag()
    {
        // NoRepeat (0x4000) should not appear in the label — it's internal.
        string label = AppSettings.FormatHotkey(
            Win32Constants.MOD_WIN | Win32Constants.MOD_ALT | Win32Constants.MOD_NOREPEAT,
            0x58);

        Assert.Equal("Win+Alt+X", label);
        Assert.DoesNotContain("NoRepeat", label);
    }

    [Fact]
    public void FormatHotkey_AllModifiers_InCanonicalOrder()
    {
        string label = AppSettings.FormatHotkey(
            Win32Constants.MOD_WIN | Win32Constants.MOD_CONTROL |
            Win32Constants.MOD_SHIFT | Win32Constants.MOD_ALT,
            0x41); // 'A'

        // Canonical order: Win, Ctrl, Shift, Alt, then key.
        Assert.Equal("Win+Ctrl+Shift+Alt+A", label);
    }

    [Fact]
    public void FormatHotkey_NoModifiers_JustKey()
    {
        string label = AppSettings.FormatHotkey(0, 0x41);
        Assert.Equal("A", label);
    }

    [Theory]
    [InlineData(0x08, "Backspace")]
    [InlineData(0x09, "Tab")]
    [InlineData(0x0D, "Enter")]
    [InlineData(0x1B, "Esc")]
    [InlineData(0x20, "Space")]
    [InlineData(0x30, "0")]
    [InlineData(0x39, "9")]
    [InlineData(0x41, "A")]
    [InlineData(0x5A, "Z")]
    [InlineData(0x70, "F1")]
    [InlineData(0x7B, "F12")]
    [InlineData(0xBA, ";")]
    [InlineData(0xBB, "+")]
    [InlineData(0xBC, ",")]
    [InlineData(0xBD, "-")]
    [InlineData(0xBE, ".")]
    [InlineData(0xBF, "/")]
    [InlineData(0xC0, "`")]
    [InlineData(0xDB, "[")]
    [InlineData(0xDC, "\\")]
    [InlineData(0xDD, "]")]
    public void KeyToString_MapsKnownKeys(uint key, string expected)
    {
        Assert.Equal(expected, AppSettings.KeyToString(key));
    }

    [Fact]
    public void KeyToString_UnknownKey_ReturnsHexCode()
    {
        string result = AppSettings.KeyToString(0xFF);
        Assert.Equal("0xFF", result);
    }

    [Fact]
    public void KeyToString_F1ThroughF12_AreSequential()
    {
        for (uint i = 0; i <= 11; i++)
        {
            string label = AppSettings.KeyToString(0x70 + i);
            Assert.Equal($"F{i + 1}", label);
        }
    }
}

/// <summary>
/// Tests for <see cref="AppSettings"/> load/save round-trip with a temp file.
/// Uses the internal <c>Load(string)</c> / <c>Save(string)</c> overloads to
/// redirect to a temp path — no env-var manipulation needed.
/// </summary>
public class AppSettingsPersistenceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _settingsPath;

    public AppSettingsPersistenceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AltTabExcluderSettingsTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _settingsPath = Path.Combine(_tempDir, "settings.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort cleanup */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Defaults_AreWinAltX_Enabled()
    {
        var settings = new AppSettings();
        Assert.Equal(Win32Constants.DefaultHotkeyModifiers, settings.HotkeyModifiers);
        Assert.Equal(Win32Constants.DefaultHotkeyKey, settings.HotkeyKey);
        Assert.True(settings.HotkeyEnabled);
        Assert.Empty(settings.ExcludedByUs);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var settings = new AppSettings
        {
            HotkeyModifiers = Win32Constants.MOD_CONTROL | Win32Constants.MOD_SHIFT,
            HotkeyKey = 0x44, // 'D'
            HotkeyEnabled = false,
            ExcludedByUs = new List<long> { 12345, 67890 },
        };
        settings.Save(_settingsPath);

        var loaded = AppSettings.Load(_settingsPath);

        Assert.Equal(Win32Constants.MOD_CONTROL | Win32Constants.MOD_SHIFT, loaded.HotkeyModifiers);
        Assert.Equal(0x44u, loaded.HotkeyKey);
        Assert.False(loaded.HotkeyEnabled);
        Assert.Equal(new List<long> { 12345, 67890 }, loaded.ExcludedByUs);
    }

    [Fact]
    public void Load_WhenFileMissing_ReturnsDefaults()
    {
        // Fresh temp dir — no settings.json exists.
        var settings = AppSettings.Load(_settingsPath);

        Assert.Equal(Win32Constants.DefaultHotkeyModifiers, settings.HotkeyModifiers);
        Assert.Equal(Win32Constants.DefaultHotkeyKey, settings.HotkeyKey);
        Assert.True(settings.HotkeyEnabled);
    }

    [Fact]
    public void Load_WithCorruptFile_ReturnsDefaults()
    {
        File.WriteAllText(_settingsPath, "this is not valid JSON {{{");

        var settings = AppSettings.Load(_settingsPath);

        Assert.Equal(Win32Constants.DefaultHotkeyModifiers, settings.HotkeyModifiers);
        Assert.True(settings.HotkeyEnabled);
    }

    [Fact]
    public void HotkeyLabel_ReflectsCurrentHotkey()
    {
        var settings = new AppSettings
        {
            HotkeyModifiers = Win32Constants.MOD_CONTROL | Win32Constants.MOD_SHIFT,
            HotkeyKey = 0x44,
        };

        Assert.Equal("Ctrl+Shift+D", settings.HotkeyLabel);
    }

    [Fact]
    public void Save_CreatesDirectoryIfMissing()
    {
        string nestedPath = Path.Combine(_tempDir, "nested", "deep", "settings.json");
        var settings = new AppSettings { HotkeyKey = 0x41 };
        settings.Save(nestedPath);

        Assert.True(File.Exists(nestedPath));
        var loaded = AppSettings.Load(nestedPath);
        Assert.Equal(0x41u, loaded.HotkeyKey);
    }
}
