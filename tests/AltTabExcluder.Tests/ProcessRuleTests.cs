using AltTabExcluder.Services;

namespace AltTabExcluder.Tests;

/// <summary>
/// Tests for <see cref="ProcessRule"/> record semantics and JSON serialization.
/// </summary>
public class ProcessRuleTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var created = new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var rule = new ProcessRule("chrome", true, created);

        Assert.Equal("chrome", rule.ProcessName);
        Assert.True(rule.Exclude);
        Assert.Equal(created, rule.CreatedAt);
    }

    [Fact]
    public void Records_WithSameValues_AreEqual()
    {
        var created = DateTime.UtcNow;
        var rule1 = new ProcessRule("chrome", true, created);
        var rule2 = new ProcessRule("chrome", true, created);

        Assert.Equal(rule1, rule2);
        Assert.True(rule1 == rule2);
    }

    [Fact]
    public void Records_WithDifferentProcessName_AreNotEqual()
    {
        var created = DateTime.UtcNow;
        var rule1 = new ProcessRule("chrome", true, created);
        var rule2 = new ProcessRule("firefox", true, created);

        Assert.NotEqual(rule1, rule2);
    }

    [Fact]
    public void Records_WithDifferentExclude_AreNotEqual()
    {
        var created = DateTime.UtcNow;
        var rule1 = new ProcessRule("chrome", true, created);
        var rule2 = new ProcessRule("chrome", false, created);

        Assert.NotEqual(rule1, rule2);
    }

    [Fact]
    public void Records_WithDifferentCreatedAt_AreNotEqual()
    {
        var rule1 = new ProcessRule("chrome", true, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var rule2 = new ProcessRule("chrome", true, new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc));

        Assert.NotEqual(rule1, rule2);
    }

    [Fact]
    public void GetHashCode_IsConsistentForEqualRecords()
    {
        var created = DateTime.UtcNow;
        var rule1 = new ProcessRule("chrome", true, created);
        var rule2 = new ProcessRule("chrome", true, created);

        Assert.Equal(rule1.GetHashCode(), rule2.GetHashCode());
    }

    [Fact]
    public void ToString_ContainsProcessName()
    {
        var rule = new ProcessRule("chrome", true, DateTime.UtcNow);
        Assert.Contains("chrome", rule.ToString());
    }
}
