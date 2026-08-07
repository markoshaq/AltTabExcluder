using Microsoft.Win32;

namespace AltTabExcluder.Services;

/// <summary>
/// Manages the "Run at Windows startup" toggle via the per-user registry key
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>. Uses HKCU (not
/// HKLM) so no administrator privileges are required and the setting is
/// per-user, matching the asInvoker manifest policy.
/// </summary>
public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AltTabExcluder";

    /// <summary>True when a startup entry for AltTabExcluder exists in HKCU\...\Run.</summary>
    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is not null;
        }
    }

    /// <summary>
    /// Adds or removes the startup entry. The command value is quoted to survive
    /// paths containing spaces.
    /// </summary>
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (key is null)
            return;

        if (enabled)
        {
            string exe = Environment.ProcessPath ?? string.Empty;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                return;
            key.SetValue(ValueName, $"\"{exe}\"");
        }
        else
        {
            if (key.GetValue(ValueName) is not null)
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
