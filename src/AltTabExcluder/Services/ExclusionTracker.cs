using Windows.Win32;
using Windows.Win32.Foundation;

namespace AltTabExcluder.Services;

/// <summary>
/// Tracks which HWNDs AltTabExcluder has excluded from Alt+Tab. This
/// distinguishes windows <em>we</em> excluded from windows that naturally carry
/// <c>WS_EX_TOOLWINDOW</c> (tool palettes, helper windows) — only the former
/// can be un-excluded by the user.
///
/// <para>
/// Instance-based (not static) so it can be injected, tested in isolation, and
/// owned by <see cref="ExclusionService"/>. The HWND set is persisted via
/// <see cref="AppSettings"/> so Quick Exclude can identify our exclusions after
/// restart.
/// </para>
/// </summary>
public sealed class ExclusionTracker
{
    private readonly HashSet<IntPtr> _handles = new();

    /// <summary>Loads persisted excluded-by-us HWNDs into the tracking set.</summary>
    public void Load(IEnumerable<long> handles)
    {
        foreach (long h in handles)
            _handles.Add((IntPtr)h);
    }

    /// <summary>Returns the current set of excluded-by-us HWNDs for persistence.</summary>
    public IEnumerable<long> GetForSave()
        => _handles.Select(h => h.ToInt64());

    /// <summary>A snapshot of the tracked HWNDs as <see cref="IntPtr"/> values.</summary>
    public IReadOnlyCollection<IntPtr> Handles => _handles;

    /// <summary>Returns <c>true</c> if this window was excluded by AltTabExcluder.</summary>
    public bool Contains(IntPtr hwnd) => _handles.Contains(hwnd);

    /// <summary>Marks an HWND as excluded by us.</summary>
    public void Add(IntPtr hwnd) => _handles.Add(hwnd);

    /// <summary>Removes an HWND from the excluded-by-us set.</summary>
    public void Remove(IntPtr hwnd) => _handles.Remove(hwnd);

    /// <summary>
    /// Removes HWNDs from the tracking set that are no longer valid windows.
    /// Call this periodically (e.g. when the tray menu opens) to prevent stale
    /// entries from accumulating and to avoid false positives from HWND recycling.
    /// </summary>
    public void PruneStale()
    {
        _handles.RemoveWhere(h => !PInvoke.IsWindow((HWND)h));
    }

    /// <summary>Clears all tracked HWNDs.</summary>
    public void Clear() => _handles.Clear();
}
