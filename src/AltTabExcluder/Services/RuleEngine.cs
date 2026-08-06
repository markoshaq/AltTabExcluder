using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AltTabExcluder.Services;

/// <summary>
/// Persists per-process "exclude from Alt+Tab" rules to
/// <c>%APPDATA%\AltTabExcluder\rules.json</c> and exposes query/mutate APIs.
///
/// Rules are keyed by process name (case-insensitive, no extension). The file
/// is rewritten atomically (write to temp + move) so a crash mid-write cannot
/// corrupt the store.
/// </summary>
public sealed class RuleEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _filePath;
    private readonly string _dirPath;
    private readonly object _gate = new();
    private Dictionary<string, ProcessRule> _rules; // key = lowercased process name

    public RuleEngine()
    {
        _dirPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AltTabExcluder");
        _filePath = Path.Combine(_dirPath, "rules.json");
        _rules = Load();
    }

    /// <summary>Path of the persisted rules file (for diagnostics / UI display).</summary>
    public string FilePath => _filePath;

    /// <summary>Snapshot of all rules, in creation order.</summary>
    public IReadOnlyList<ProcessRule> Rules
    {
        get
        {
            lock (_gate)
                return _rules.Values.OrderBy(r => r.CreatedAt).ToList();
        }
    }

    /// <summary>
    /// Returns the rule for <paramref name="processName"/> (case-insensitive),
    /// or <c>null</c> if none exists.
    /// </summary>
    public ProcessRule? GetRule(string processName)
    {
        if (string.IsNullOrEmpty(processName))
            return null;
        lock (_gate)
            return _rules.TryGetValue(Key(processName), out var r) ? r : null;
    }

    /// <summary>
    /// Upserts a rule. If <paramref name="exclude"/> is <c>false</c> and a rule
    /// already exists, the rule is <em>removed</em> (a "show" rule is meaningless
    /// as a stored preference — the default is to show).
    /// </summary>
    public void SetRule(string processName, bool exclude)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return;

        lock (_gate)
        {
            string key = Key(processName);
            if (!exclude)
            {
                if (_rules.Remove(key))
                    Save();
                return;
            }

            _rules[key] = new ProcessRule(processName, exclude, DateTime.UtcNow);
            Save();
        }
    }

    /// <summary>Removes any rule for <paramref name="processName"/>.</summary>
    public void RemoveRule(string processName)
    {
        lock (_gate)
        {
            if (_rules.Remove(Key(processName)))
                Save();
        }
    }

    /// <summary>
    /// Applies the matching rule (if any) to a window's current style. Returns
    /// the rule that was applied, or <c>null</c> if no rule matched.
    /// </summary>
    public ProcessRule? ApplyTo(IntPtr hwnd, string processName)
    {
        var rule = GetRule(processName);
        if (rule is null)
            return null;

        WindowManager.SetExcluded(hwnd, rule.Exclude);
        return rule;
    }

    private static string Key(string processName)
        => processName.ToLowerInvariant();

    private Dictionary<string, ProcessRule> Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new Dictionary<string, ProcessRule>(StringComparer.OrdinalIgnoreCase);

            string json = File.ReadAllText(_filePath);
            var list = JsonSerializer.Deserialize<List<ProcessRule>>(json, JsonOptions);
            if (list is null)
                return new Dictionary<string, ProcessRule>(StringComparer.OrdinalIgnoreCase);

            var dict = new Dictionary<string, ProcessRule>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in list)
            {
                if (!string.IsNullOrWhiteSpace(r.ProcessName))
                    dict[Key(r.ProcessName)] = r;
            }
            return dict;
        }
        catch
        {
            // Corrupt/inaccessible file: start empty rather than crashing the app.
            return new Dictionary<string, ProcessRule>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(_dirPath);
            var list = _rules.Values.OrderBy(r => r.CreatedAt).ToList();
            string json = JsonSerializer.Serialize(list, JsonOptions);

            // Atomic write: temp file + Move to avoid partial writes on crash.
            string tmp = _filePath + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(_filePath))
                File.Replace(tmp, _filePath, destinationBackupFileName: null);
            else
                File.Move(tmp, _filePath);
        }
        catch
        {
            // Persistence is best-effort; never crash the tray app on IO failure.
        }
    }
}
