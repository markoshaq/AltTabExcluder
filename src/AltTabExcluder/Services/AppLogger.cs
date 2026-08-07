using System.Text;

namespace AltTabExcluder.Services;

/// <summary>
/// Lightweight file logger that writes to
/// <c>%APPDATA%\AltTabExcluder\app.log</c> with size-based rotation. No external
/// dependencies — uses a simple lock for thread safety and a single file handle
/// per write (tray-app volume is negligible).
///
/// <para>
/// Log rotation: when the log file exceeds <see cref="MaxFileBytes"/>, it is
/// moved to <c>app.log.bak</c> (overwriting any previous backup) and a fresh
/// log is started. This keeps the log bounded to ~2× <see cref="MaxFileBytes"/>.
/// </para>
/// <para>
/// All methods are safe to call from any thread. Failures are silently
/// swallowed — logging must never crash the tray app.
/// </para>
/// </summary>
public static class AppLogger
{
    private static readonly string DefaultDirPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AltTabExcluder");

    // These are not readonly so tests can redirect them via SetLogDirectory.
    private static string _dirPath = DefaultDirPath;
    private static string _filePath = Path.Combine(DefaultDirPath, "app.log");
    private static string _backupPath = Path.Combine(DefaultDirPath, "app.log.bak");

    /// <summary>Maximum log file size in bytes before rotation (256 KB).</summary>
    private const long MaxFileBytes = 256 * 1024;

    private static readonly object _gate = new();
    private static bool _dirEnsured;

    /// <summary>Log severity levels, ordered from least to most severe.</summary>
    public enum Level
    {
        Debug,
        Info,
        Warning,
        Error,
    }

    /// <summary>
    /// Minimum level to write to the log. Defaults to <see cref="Level.Info"/>
    /// in Release and <see cref="Level.Debug"/> in Debug. Can be adjusted at
    /// runtime (e.g. via a future settings option).
    /// </summary>
    public static Level MinimumLevel { get; set; } =
#if DEBUG
        Level.Debug;
#else
        Level.Info;
#endif

    /// <summary>
    /// Redirects the log directory to <paramref name="dir"/>. Internal — used
    /// by tests to write to a temp directory instead of the real %APPDATA%.
    /// Resets the directory-ensured flag so the new directory is created on
    /// the next write. Must be called before any Log* method.
    /// </summary>
    internal static void SetLogDirectory(string dir)
    {
        lock (_gate)
        {
            _dirPath = dir;
            _filePath = Path.Combine(dir, "app.log");
            _backupPath = Path.Combine(dir, "app.log.bak");
            _dirEnsured = false;
        }
    }

    /// <summary>Current log file path (for diagnostics / tests).</summary>
    internal static string LogFilePath => _filePath;

    /// <summary>Current backup log file path (for diagnostics / tests).</summary>
    internal static string BackupFilePath => _backupPath;

    public static void LogDebug(string message) => Log(Level.Debug, message);
    public static void LogInfo(string message) => Log(Level.Info, message);
    public static void LogWarning(string message) => Log(Level.Warning, message);
    public static void LogError(string message) => Log(Level.Error, message);

    /// <summary>Logs an exception with its type, message, and stack trace.</summary>
    public static void LogException(Exception ex, string? context = null)
    {
        string msg = context is not null
            ? $"{context}: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}"
            : $"{ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}";
        Log(Level.Error, msg);
    }

    /// <summary>
    /// Logs a caught exception at Warning level (for expected/recoverable
    /// exceptions) with optional context.
    /// </summary>
    public static void LogWarning(Exception ex, string? context = null)
    {
        string msg = context is not null
            ? $"{context}: {ex.GetType().Name}: {ex.Message}"
            : $"{ex.GetType().Name}: {ex.Message}";
        Log(Level.Warning, msg);
    }

    private static void Log(Level level, string message)
    {
        if (level < MinimumLevel)
            return;

        string line = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} [{level,-7}] {message}{Environment.NewLine}";

        lock (_gate)
        {
            try
            {
                EnsureDir();
                RotateIfNeeded();
                File.AppendAllText(_filePath, line, Encoding.UTF8);
            }
            catch
            {
                // Logging must never crash the app.
            }
        }
    }

    private static void EnsureDir()
    {
        if (_dirEnsured) return;
        Directory.CreateDirectory(_dirPath);
        _dirEnsured = true;
    }

    private static void RotateIfNeeded()
    {
        try
        {
            if (!File.Exists(_filePath))
                return;

            var info = new FileInfo(_filePath);
            if (info.Length < MaxFileBytes)
                return;

            // Move current log to backup (overwrite any existing backup).
            if (File.Exists(_backupPath))
                File.Delete(_backupPath);
            File.Move(_filePath, _backupPath);
        }
        catch
        {
            // Rotation is best-effort — don't block the log write.
        }
    }
}
