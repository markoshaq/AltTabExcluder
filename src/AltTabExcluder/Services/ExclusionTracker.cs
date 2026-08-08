using Windows.Win32;
using Windows.Win32.Foundation;

namespace AltTabExcluder.Services;

/// <summary>
/// Tracks which HWNDs AltTabExcluder has excluded from Alt+Tab, along with the
/// process ID (PID) at the time of exclusion. This distinguishes windows
/// <em>we</em> excluded from windows that naturally carry
/// <c>WS_EX_TOOLWINDOW</c> (tool palettes, helper windows) — only the former
/// can be un-excluded by the user.
///
/// <para>
/// The PID is stored alongside the HWND to detect HWND recycling: after a
/// window closes, the OS can reuse the same HWND value for a new, unrelated
/// window. By comparing the stored PID with the window's current PID, we can
/// detect that the HWND was recycled and treat it as stale.
/// </para>
/// <para>
/// Instance-based (not static) so it can be injected, tested in isolation, and
/// owned by <see cref="ExclusionService"/>. The HWND+PID set is persisted via
/// <see cref="AppSettings"/> so Quick Exclude can identify our exclusions after
/// restart.
/// </para>
/// </summary>
public sealed class ExclusionTracker
{
    /// <summary>
    /// A tracked excluded HWND with the PID recorded at exclusion time.
    /// A PID of 0 means "unknown" (loaded from an old settings file).
    /// </summary>
    public sealed record TrackedHandle(IntPtr Hwnd, uint Pid);

    private readonly Dictionary<IntPtr, uint> _handles = new();

    /// <summary>
    /// Loads persisted excluded-by-us HWND+PID pairs into the tracking set.
    /// Each tuple is (HWND as long, PID as uint).
    /// </summary>
    public void Load(IEnumerable<(long hwnd, uint pid)> entries)
    {
        foreach (var (hwnd, pid) in entries)
            _handles[(IntPtr)hwnd] = pid;
    }

    /// <summary>
    /// Loads persisted excluded-by-us HWNDs without PID information (backward
    /// compatibility with the old flat-array settings format). PID is set to 0
    /// (unknown) — <see cref="WasExcludedByUs"/> will treat PID 0 as "always
    /// match" until the next save.
    /// </summary>
    public void Load(IEnumerable<long> handles)
    {
        foreach (long h in handles)
            _handles[(IntPtr)h] = 0;
    }

    /// <summary>Returns the current set of excluded-by-us HWND+PID pairs for persistence.</summary>
    public IEnumerable<(long hwnd, uint pid)> GetForSave()
        => _handles.Select(kv => (kv.Key.ToInt64(), kv.Value));

    /// <summary>A snapshot of the tracked HWNDs as <see cref="TrackedHandle"/> records.</summary>
    public IReadOnlyCollection<TrackedHandle> Handles
        => _handles.Select(kv => new TrackedHandle(kv.Key, kv.Value)).ToList().AsReadOnly();

    /// <summary>
    /// Returns <c>true</c> if this window was excluded by AltTabExcluder.
    /// Does NOT check PID — use <see cref="WasExcludedByUs"/> for identity-aware
    /// checking.
    /// </summary>
    public bool Contains(IntPtr hwnd) => _handles.ContainsKey(hwnd);

    /// <summary>
    /// Returns <c>true</c> if this window was excluded by AltTabExcluder AND
    /// the window's current PID matches the stored PID. If the stored PID is 0
    /// (unknown, from an old settings file), returns <c>true</c> as long as the
    /// HWND is in the tracker (backward-compatible behavior).
    /// </summary>
    public bool WasExcludedByUs(IntPtr hwnd, uint currentPid)
    {
        if (!_handles.TryGetValue(hwnd, out uint storedPid))
            return false;
        // PID 0 means "unknown" (loaded from old format) — trust the HWND.
        if (storedPid == 0)
            return true;
        return storedPid == currentPid;
    }

    /// <summary>Marks an HWND as excluded by us, recording the PID.</summary>
    public void Add(IntPtr hwnd, uint pid) => _handles[hwnd] = pid;

    /// <summary>
    /// Marks an HWND as excluded by us with PID lookup via
    /// <c>GetWindowThreadProcessId</c>. Convenience overload for callers that
    /// don't already have the PID.
    /// </summary>
    public void Add(IntPtr hwnd)
    {
        uint pid = GetPidForHwnd(hwnd);
        _handles[hwnd] = pid;
    }

    /// <summary>Removes an HWND from the excluded-by-us set.</summary>
    public void Remove(IntPtr hwnd) => _handles.Remove(hwnd);

    /// <summary>
    /// Removes HWNDs from the tracking set that are no longer valid windows.
    /// Call this periodically (e.g. when the tray menu opens) to prevent stale
    /// entries from accumulating and to avoid false positives from HWND recycling.
    /// </summary>
    public void PruneStale()
    {
        var stale = _handles.Where(kv => !PInvoke.IsWindow((HWND)kv.Key))
            .Select(kv => kv.Key)
            .ToList();
        foreach (var h in stale)
            _handles.Remove(h);
    }

    /// <summary>Clears all tracked HWNDs.</summary>
    public void Clear() => _handles.Clear();

    /// <summary>Gets the PID for the given HWND via GetWindowThreadProcessId.</summary>
    private static uint GetPidForHwnd(IntPtr hwnd)
    {
        try
        {
            unsafe
            {
                uint pid;
                _ = PInvoke.GetWindowThreadProcessId((HWND)hwnd, &pid);
                return pid;
            }
        }
        catch
        {
            return 0;
        }
    }
}
