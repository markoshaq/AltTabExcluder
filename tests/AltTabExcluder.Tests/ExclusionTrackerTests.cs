using AltTabExcluder.Services;

namespace AltTabExcluder.Tests;

/// <summary>
/// Tests for <see cref="ExclusionTracker"/> — the instance-based replacement
/// for the former static ExcludedByUs HashSet. These tests cover the pure
/// (non-Win32) operations: Add, Remove, Contains, Load, GetForSave, Clear.
/// PruneStale is not tested here because it calls IsWindow (Win32).
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

        tracker.Add(hwnd);

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
        tracker.Add(hwnd);

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
    public void Add_DuplicateHandle_DoesNotCreateSecondEntry()
    {
        var tracker = new ExclusionTracker();
        var hwnd = new IntPtr(12345);

        tracker.Add(hwnd);
        tracker.Add(hwnd);

        Assert.Single(tracker.Handles);
    }

    [Fact]
    public void Load_PopulatesFromLongValues()
    {
        var tracker = new ExclusionTracker();
        var handles = new long[] { 100, 200, 300 };

        tracker.Load(handles);

        Assert.Equal(3, tracker.Handles.Count);
        Assert.True(tracker.Contains(new IntPtr(100)));
        Assert.True(tracker.Contains(new IntPtr(200)));
        Assert.True(tracker.Contains(new IntPtr(300)));
    }

    [Fact]
    public void Load_ThenGetForSave_RoundTrips()
    {
        var tracker = new ExclusionTracker();
        var handles = new long[] { 100, 200, 300 };

        tracker.Load(handles);
        var saved = tracker.GetForSave().ToList();

        Assert.Equal(handles.OrderBy(h => h), saved.OrderBy(h => h));
    }

    [Fact]
    public void Add_ThenGetForSave_ReturnsIntPtrAsLong()
    {
        var tracker = new ExclusionTracker();
        var hwnd = new IntPtr(0x1234);

        tracker.Add(hwnd);

        var saved = tracker.GetForSave().ToList();
        Assert.Single(saved);
        Assert.Equal(0x1234L, saved[0]);
    }

    [Fact]
    public void Clear_RemovesAllHandles()
    {
        var tracker = new ExclusionTracker();
        tracker.Add(new IntPtr(1));
        tracker.Add(new IntPtr(2));
        tracker.Add(new IntPtr(3));

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
        tracker.Load(Array.Empty<long>());
        Assert.Empty(tracker.Handles);
    }

    [Fact]
    public void Load_CanBeCalledMultipleTimes_Accumulates()
    {
        var tracker = new ExclusionTracker();
        tracker.Load(new long[] { 100 });
        tracker.Load(new long[] { 200 });

        Assert.Equal(2, tracker.Handles.Count);
    }

    [Fact]
    public void Load_DuplicateValues_AreDeduplicated()
    {
        var tracker = new ExclusionTracker();
        tracker.Load(new long[] { 100, 100, 200 });

        Assert.Equal(2, tracker.Handles.Count);
    }
}
