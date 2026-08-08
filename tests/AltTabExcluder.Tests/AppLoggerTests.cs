using AltTabExcluder.Services;

namespace AltTabExcluder.Tests;

/// <summary>
/// Tests for <see cref="AppLogger"/>. Uses <see cref="AppLogger.SetLogDirectory"/>
/// to redirect to a temp directory so tests don't write to the real %APPDATA%.
/// </summary>
public class AppLoggerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppLogger.Level _originalMinLevel;

    public AppLoggerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AltTabExcluderLogTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _originalMinLevel = AppLogger.MinimumLevel;
        AppLogger.SetLogDirectory(_tempDir);
        AppLogger.MinimumLevel = AppLogger.Level.Debug;
    }

    public void Dispose()
    {
        AppLogger.MinimumLevel = _originalMinLevel;
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort cleanup */ }
        GC.SuppressFinalize(this);
    }

    private string LogPath => Path.Combine(_tempDir, "app.log");

    [Fact]
    public void LogInfo_WritesLineToFile()
    {
        AppLogger.LogInfo("test message");
        Assert.True(File.Exists(LogPath));
        string content = File.ReadAllText(LogPath);
        Assert.Contains("test message", content);
        Assert.Contains("[Info   ]", content);
    }

    [Fact]
    public void LogDebug_WritesWhenMinimumLevelIsDebug()
    {
        AppLogger.MinimumLevel = AppLogger.Level.Debug;
        AppLogger.LogDebug("debug msg");
        Assert.True(File.Exists(LogPath));
        Assert.Contains("debug msg", File.ReadAllText(LogPath));
    }

    [Fact]
    public void LogDebug_DoesNotWriteWhenMinimumLevelIsInfo()
    {
        AppLogger.MinimumLevel = AppLogger.Level.Info;
        AppLogger.LogDebug("should not appear");
        Assert.False(File.Exists(LogPath));
    }

    [Fact]
    public void LogWarning_WritesWarningLevel()
    {
        AppLogger.LogWarning("warning msg");
        Assert.True(File.Exists(LogPath));
        string content = File.ReadAllText(LogPath);
        Assert.Contains("warning msg", content);
        Assert.Contains("[Warning]", content);
    }

    [Fact]
    public void LogError_WritesErrorLevel()
    {
        AppLogger.LogError("error msg");
        Assert.True(File.Exists(LogPath));
        string content = File.ReadAllText(LogPath);
        Assert.Contains("error msg", content);
        Assert.Contains("[Error  ]", content);
    }

    [Fact]
    public void LogException_WritesExceptionTypeAndMessage()
    {
        var ex = new InvalidOperationException("something broke");
        AppLogger.LogException(ex, "context here");
        Assert.True(File.Exists(LogPath));
        string content = File.ReadAllText(LogPath);
        Assert.Contains("InvalidOperationException", content);
        Assert.Contains("something broke", content);
        Assert.Contains("context here", content);
    }

    [Fact]
    public void LogException_WithoutContext_WritesExceptionTypeAndMessage()
    {
        var ex = new ArgumentException("bad arg");
        AppLogger.LogException(ex);
        Assert.True(File.Exists(LogPath));
        string content = File.ReadAllText(LogPath);
        Assert.Contains("ArgumentException", content);
        Assert.Contains("bad arg", content);
    }

    [Fact]
    public void LogWarning_WithException_WritesExceptionTypeAndMessage()
    {
        var ex = new IOException("io failed");
        AppLogger.LogWarning(ex, "during save");
        Assert.True(File.Exists(LogPath));
        string content = File.ReadAllText(LogPath);
        Assert.Contains("IOException", content);
        Assert.Contains("io failed", content);
        Assert.Contains("during save", content);
        Assert.Contains("[Warning]", content);
    }

    [Fact]
    public void LogWarning_WithException_WithoutContext_WritesExceptionType()
    {
        var ex = new FormatException("bad format");
        AppLogger.LogWarning(ex);
        Assert.True(File.Exists(LogPath));
        string content = File.ReadAllText(LogPath);
        Assert.Contains("FormatException", content);
        Assert.Contains("bad format", content);
    }

    [Fact]
    public void MultipleLogCalls_AppendToSameFile()
    {
        AppLogger.LogInfo("first");
        AppLogger.LogInfo("second");
        AppLogger.LogInfo("third");

        string content = File.ReadAllText(LogPath);
        Assert.Contains("first", content);
        Assert.Contains("second", content);
        Assert.Contains("third", content);
    }

    [Fact]
    public void LogInfo_CreatesDirectoryIfMissing()
    {
        string nestedDir = Path.Combine(_tempDir, "nested", "deep");
        AppLogger.SetLogDirectory(nestedDir);
        AppLogger.LogInfo("in nested dir");

        Assert.True(File.Exists(Path.Combine(nestedDir, "app.log")));
    }

    [Fact]
    public void SetLogDirectory_ResetsToNewDirectory()
    {
        AppLogger.LogInfo("in original dir");
        Assert.True(File.Exists(LogPath));

        string newDir = Path.Combine(_tempDir, "second");
        AppLogger.SetLogDirectory(newDir);
        AppLogger.LogInfo("in new dir");

        Assert.True(File.Exists(Path.Combine(newDir, "app.log")));
    }

    [Fact]
    public void LogFilePath_ReturnsCorrectPath()
    {
        Assert.Equal(Path.Combine(_tempDir, "app.log"), AppLogger.LogFilePath);
    }

    [Fact]
    public void BackupFilePath_ReturnsCorrectPath()
    {
        Assert.Equal(Path.Combine(_tempDir, "app.log.bak"), AppLogger.BackupFilePath);
    }
}
