using AltTabExcluder.Services;

namespace AltTabExcluder.Tests;

/// <summary>
/// Tests for <see cref="ExclusionTracker"/> — the instance-based replacement
/// for the former static ExcludedByUs HashSet. These tests cover the pure
/// (non-Win32) operations: Add, Remove, Contains, WasExcludedByUs (PID match),
/// Load, GetForSave, Clear. PruneStale is not tested here because it calls
/// IsWindow (Win32).
/// </summary>
public class ExclusionTrackerTests
{
    [Fact]
    public void NewTracker_IsEmpty()
    {
        var tracker = new ExclusionTracker();
        Assert.Empty(tracker.Handles);
    }

    [Fact]
    public void Add_ThenContains_ReturnsTrue()
    {
        var tracker = new ExclusionTracker();
        var hwnd = new IntPtr(12345);

        tracker.Add(hwnd, pid: 100);

        Assert.True(tracker.Contains(hwnd));
        Assert.Single(tracker.Handles);
    }

    [Fact]
    public void Contains_WhenNotAdded_ReturnsFalse()
    {
        var tracker = new ExclusionTracker();
        Assert.False(tracker.Contains(new IntPtr(999)));
    }

    [Fact]
    public void Remove_ThenContains_ReturnsFalse()
    {
        var tracker = new ExclusionTracker();
        var hwnd = new IntPtr(12345);
        tracker.Add(hwnd, pid: 100);

        tracker.Remove(hwnd);

        Assert.False(tracker.Contains(hwnd));
        Assert.Empty(tracker.Handles);
    }

    [Fact]
    public void Remove_WhenNotPresent_IsNoOp()
    {
        var tracker = new ExclusionTracker();
        tracker.Remove(new IntPtr(999));
        Assert.Empty(tracker.Handles);
    }

    [Fact]
    public void Add_DuplicateHandle_UpdatesPid()
    {
        var tracker = new ExclusionTracker();
        var hwnd = new IntPtr(12345);

        tracker.Add(hwnd, pid: 100);
        tracker.Add(hwnd, pid: 200);

        Assert.Single(tracker.Handles);
        var entry = tracker.Handles.First(h => h.Hwnd == hwnd);
        Assert.Equal(200u, entry.Pid);
    }

    [Fact]
    public void Load_WithPidTuples_PopulatesFromEntries()
    {
        var tracker = new ExclusionTracker();
        var entries = new (long hwnd, uint pid)[] { (100, 10), (200, 20), (300, 30) };

        tracker.Load(entries);

        Assert.Equal(3, tracker.Handles.Count);
        Assert.True(tracker.Contains(new IntPtr(100)));
        Assert.True(tracker.Contains(new IntPtr(200)));
        Assert.True(tracker.Contains(new IntPtr(300)));
    }

    [Fact]
    public void Load_WithPidTuples_ThenGetForSave_RoundTrips()
    {
        var tracker = new ExclusionTracker();
        var entries = new (long hwnd, uint pid)[] { (100, 10), (200, 20), (300, 30) };

        tracker.Load(entries);
        var saved = tracker.GetForSave().ToList();

        Assert.Equal(3, saved.Count);
        Assert.Contains((100L, 10u), saved);
        Assert.Contains((200L, 20u), saved);
        Assert.Contains((300L, 30u), saved);
    }

    [Fact]
    public void Add_ThenGetForSave_ReturnsHwndAndPid()
    {
        var tracker = new ExclusionTracker();
        var hwnd = new IntPtr(0x1234);

        tracker.Add(hwnd, pid: 42);

        var saved = tracker.GetForSave().ToList();
        Assert.Single(saved);
        Assert.Equal(0x1234L, saved[0].hwnd);
        Assert.Equal(42u, saved[0].pid);
    }

    [Fact]
    public void Clear_RemovesAllHandles()
    {
        var tracker = new ExclusionTracker();
        tracker.Add(new IntPtr(1), pid: 10);
        tracker.Add(new IntPtr(2), pid: 20);
        tracker.Add(new IntPtr(3), pid: 30);

        tracker.Clear();

        Assert.Empty(tracker.Handles);
    }

    [Fact]
    public void Clear_WhenEmpty_IsNoOp()
    {
        var tracker = new ExclusionTracker();
        tracker.Clear();
        Assert.Empty(tracker.Handles);
    }

    [Fact]
    public void Load_WithEmptyCollection_LeavesTrackerEmpty()
    {
        var tracker = new ExclusionTracker();
        tracker.Load(Array.Empty<(long, uint)>());
        Assert.Empty(tracker.Handles);
    }

    [Fact]
    public void Load_CanBeCalledMultipleTimes_Accumulates()
    {
        var tracker = new ExclusionTracker();
        tracker.Load(new (long, uint)[] { (100, 10) });
        tracker.Load(new (long, uint)[] { (200, 20) });

        Assert.Equal(2, tracker.Handles.Count);
    }

    [Fact]
    public void Load_DuplicateValues_AreDeduplicated_LastPidWins()
    {
        var tracker = new ExclusionTracker();
        tracker.Load(new (long, uint)[] { (100, 10), (100, 20), (200, 30) });

        Assert.Equal(2, tracker.Handles.Count);
        var entry100 = tracker.Handles.First(h => h.Hwnd == new IntPtr(100));
        Assert.Equal(20u, entry100.Pid);
    }

    // ─── Backward compat: Load(IEnumerable<long>) ────────────────────────

    [Fact]
    public void Load_WithOldFlatLongFormat_PopulatesWithPidZero()
    {
        var tracker = new ExclusionTracker();
        tracker.Load(new long[] { 100, 200, 300 });

        Assert.Equal(3, tracker.Handles.Count);
        Assert.True(tracker.Contains(new IntPtr(100)));
        var entry = tracker.Handles.First(h => h.Hwnd == new IntPtr(100));
        Assert.Equal(0u, entry.Pid); // PID unknown from old format
    }

    // ─── WasExcludedByUs (PID-aware identity check) ──────────────────────

    [Fact]
    public void WasExcludedByUs_MatchingPid_ReturnsTrue()
    {
        var tracker = new ExclusionTracker();
        tracker.Add(new IntPtr(12345), pid: 100);

        Assert.True(tracker.WasExcludedByUs(new IntPtr(12345), currentPid: 100));
    }

    [Fact]
    public void WasExcludedByUs_DifferentPid_ReturnsFalse()
    {
        // Simulates HWND recycling: same HWND value, different process.
        var tracker = new ExclusionTracker();
        tracker.Add(new IntPtr(12345), pid: 100);

        Assert.False(tracker.WasExcludedByUs(new IntPtr(12345), currentPid: 999));
    }

    [Fact]
    public void WasExcludedByUs_NotInTracker_ReturnsFalse()
    {
        var tracker = new ExclusionTracker();

        Assert.False(tracker.WasExcludedByUs(new IntPtr(999), currentPid: 100));
    }

    [Fact]
    public void WasExcludedByUs_StoredPidZero_AlwaysReturnsTrue()
    {
        // Backward compat: PID 0 means "unknown" (from old settings format).
        // In this case, trust the HWND alone.
        var tracker = new ExclusionTracker();
        tracker.Add(new IntPtr(12345), pid: 0);

        Assert.True(tracker.WasExcludedByUs(new IntPtr(12345), currentPid: 999));
        Assert.True(tracker.WasExcludedByUs(new IntPtr(12345), currentPid: 100));
    }
}
