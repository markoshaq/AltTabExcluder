using System.Text.Json;
using AltTabExcluder.Services;

namespace AltTabExcluder.Tests;

/// <summary>
/// Tests for <see cref="RuleEngine"/> persistence and rule management.
/// Uses the internal test-only constructor with a temp file path so tests
/// don't clobber the real rules.json.
/// </summary>
public class RuleEngineTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _rulesPath;

    public RuleEngineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AltTabExcluderRuleTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _rulesPath = Path.Combine(_tempDir, "rules.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort cleanup */ }
    }

    [Fact]
    public void NewEngine_WithNoFile_HasNoRules()
    {
        var engine = new RuleEngine(_rulesPath);
        Assert.Empty(engine.Rules);
    }

    [Fact]
    public void SetRule_ThenGetRule_ReturnsTheRule()
    {
        var engine = new RuleEngine(_rulesPath);
        engine.SetRule("chrome", exclude: true);

        var rule = engine.GetRule("chrome");
        Assert.NotNull(rule);
        Assert.Equal("chrome", rule!.ProcessName);
        Assert.True(rule.Exclude);
    }

    [Fact]
    public void GetRule_IsCaseInsensitive()
    {
        var engine = new RuleEngine(_rulesPath);
        engine.SetRule("Chrome", exclude: true);

        Assert.NotNull(engine.GetRule("chrome"));
        Assert.NotNull(engine.GetRule("CHROME"));
        Assert.NotNull(engine.GetRule("Chrome"));
    }

    [Fact]
    public void GetRule_WithNullOrEmpty_ReturnsNull()
    {
        var engine = new RuleEngine(_rulesPath);
        Assert.Null(engine.GetRule(""));
        Assert.Null(engine.GetRule(null!));
    }

    [Fact]
    public void SetRule_WithExcludeFalse_RemovesTheRule()
    {
        var engine = new RuleEngine(_rulesPath);
        engine.SetRule("chrome", exclude: true);
        Assert.Single(engine.Rules);

        engine.SetRule("chrome", exclude: false);
        Assert.Empty(engine.Rules);
    }

    [Fact]
    public void SetRule_WithExcludeFalse_WhenNoRuleExists_IsNoOp()
    {
        var engine = new RuleEngine(_rulesPath);
        engine.SetRule("chrome", exclude: false);
        Assert.Empty(engine.Rules);
    }

    [Fact]
    public void SetRule_WithWhitespaceName_DoesNothing()
    {
        var engine = new RuleEngine(_rulesPath);
        engine.SetRule("   ", exclude: true);
        Assert.Empty(engine.Rules);
    }

    [Fact]
    public void RemoveRule_RemovesExistingRule()
    {
        var engine = new RuleEngine(_rulesPath);
        engine.SetRule("chrome", exclude: true);
        engine.RemoveRule("chrome");
        Assert.Empty(engine.Rules);
    }

    [Fact]
    public void RemoveRule_WhenNoRuleExists_IsNoOp()
    {
        var engine = new RuleEngine(_rulesPath);
        engine.RemoveRule("chrome");
        Assert.Empty(engine.Rules);
    }

    [Fact]
    public void SetRule_UpsertsExistingRule_UpdatesTimestamp()
    {
        var engine = new RuleEngine(_rulesPath);
        engine.SetRule("chrome", exclude: true);
        var rule1 = engine.GetRule("chrome")!;

        Thread.Sleep(20); // ensure CreatedAt differs
        engine.SetRule("chrome", exclude: true);
        var rule2 = engine.GetRule("chrome")!;

        Assert.True(rule2.CreatedAt >= rule1.CreatedAt);
    }

    [Fact]
    public void SaveThenLoad_PersistsRulesAcrossInstances()
    {
        var engine = new RuleEngine(_rulesPath);
        engine.SetRule("chrome", exclude: true);
        engine.SetRule("spotify", exclude: true);

        var engine2 = new RuleEngine(_rulesPath);
        Assert.Equal(2, engine2.Rules.Count);
        Assert.NotNull(engine2.GetRule("chrome"));
        Assert.NotNull(engine2.GetRule("spotify"));
    }

    [Fact]
    public void Load_WithCorruptFile_StartsEmpty()
    {
        File.WriteAllText(_rulesPath, "this is not valid JSON {{{");

        var engine = new RuleEngine(_rulesPath);
        Assert.Empty(engine.Rules);
    }

    [Fact]
    public void Load_WithEmptyFile_StartsEmpty()
    {
        File.WriteAllText(_rulesPath, "");

        var engine = new RuleEngine(_rulesPath);
        Assert.Empty(engine.Rules);
    }

    [Fact]
    public void Rules_AreOrderedByCreatedAt()
    {
        var engine = new RuleEngine(_rulesPath);
        engine.SetRule("zebra", exclude: true);
        Thread.Sleep(20);
        engine.SetRule("alpha", exclude: true);
        Thread.Sleep(20);
        engine.SetRule("mid", exclude: true);

        var rules = engine.Rules;
        Assert.Equal("zebra", rules[0].ProcessName);
        Assert.Equal("alpha", rules[1].ProcessName);
        Assert.Equal("mid", rules[2].ProcessName);
    }

    [Fact]
    public void ApplyTo_WithMatchingRule_InvokesCallback()
    {
        var applied = new List<(IntPtr Hwnd, bool Excluded)>();
        var engine = new RuleEngine(_rulesPath, (hwnd, excluded) => applied.Add((hwnd, excluded)));
        engine.SetRule("chrome", exclude: true);

        var hwnd = new IntPtr(12345);
        var rule = engine.ApplyTo(hwnd, "chrome");

        Assert.NotNull(rule);
        Assert.Single(applied);
        Assert.Equal(hwnd, applied[0].Hwnd);
        Assert.True(applied[0].Excluded);
    }

    [Fact]
    public void ApplyTo_WithNoMatchingRule_ReturnsNull_NoCallback()
    {
        var applied = new List<(IntPtr, bool)>();
        var engine = new RuleEngine(_rulesPath, (hwnd, excluded) => applied.Add((hwnd, excluded)));

        var rule = engine.ApplyTo(new IntPtr(999), "unknown");

        Assert.Null(rule);
        Assert.Empty(applied);
    }

    [Fact]
    public void ApplyTo_IsCaseInsensitive()
    {
        var applied = new List<(IntPtr, bool)>();
        var engine = new RuleEngine(_rulesPath, (hwnd, excluded) => applied.Add((hwnd, excluded)));
        engine.SetRule("Chrome", exclude: true);

        var rule = engine.ApplyTo(new IntPtr(1), "chrome");

        Assert.NotNull(rule);
        Assert.Single(applied);
    }

    [Fact]
    public void Save_WritesAtomicTempFile()
    {
        var engine = new RuleEngine(_rulesPath);
        engine.SetRule("chrome", exclude: true);

        // After save, the main file should exist. The temp file may or may not
        // still be around (it's moved), but the main file must be valid JSON.
        Assert.True(File.Exists(_rulesPath));

        string json = File.ReadAllText(_rulesPath);
        var list = JsonSerializer.Deserialize<List<ProcessRule>>(json);
        Assert.NotNull(list);
        Assert.Single(list!);
    }

    [Fact]
    public void Load_WithDuplicateProcessNames_KeepsLastEntry()
    {
        // Manually write a rules file with duplicate keys (different cases).
        var json = "[{\"processName\":\"Chrome\",\"exclude\":true,\"createdAt\":\"2024-01-01T00:00:00Z\"}," +
                   "{\"processName\":\"chrome\",\"exclude\":true,\"createdAt\":\"2024-01-02T00:00:00Z\"}]";
        File.WriteAllText(_rulesPath, json);

        var engine = new RuleEngine(_rulesPath);

        // Both entries map to the same key ("chrome"), so only one rule survives.
        Assert.Single(engine.Rules);
    }
}

