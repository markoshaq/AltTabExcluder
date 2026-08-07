namespace AltTabExcluder.Services;

/// <summary>
/// A persistent rule mapping a process name (e.g. <c>spotify</c>) to an
/// "exclude from Alt+Tab" preference. Rules are applied automatically when a
/// matching window appears (see <see cref="WindowEventWatcher"/>).
/// </summary>
/// <param name="ProcessName">
/// The process name without extension and without case sensitivity
/// (e.g. <c>Spotify</c>). Compared case-insensitively at apply time.
/// </param>
/// <param name="Exclude">True to hide matching windows from Alt+Tab.</param>
/// <param name="CreatedAt">UTC timestamp the rule was first created.</param>
public sealed record ProcessRule(
    string ProcessName,
    bool Exclude,
    DateTime CreatedAt);
