using AltTabExcluder.Services;

namespace AltTabExcluder.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="ExclusionService"/> — exercises the full
/// orchestration of <see cref="WindowManager"/> + <see cref="ExclusionTracker"/>
/// against real (test-created) windows. Tagged "Integration" so they can be
/// filtered out of headless runs.
/// </summary>
[Trait("Category", "Integration")]
public class ExclusionServiceIntegrationTests : IDisposable
{
    private readonly Form _form;
    private readonly ExclusionTracker _tracker;
    private readonly ExclusionService _exclusion;

    public ExclusionServiceIntegrationTests()
    {
        _form = IntegrationTestHelper.CreateTestForm("ES_Integration_Test");
        _tracker = new ExclusionTracker();
        _exclusion = new ExclusionService(_tracker);
    }

    public void Dispose()
    {
        // Restore any windows we excluded so we don't leave the desktop in a
        // modified state.
        try { _exclusion.RestoreAll(); } catch { }
        _form?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Toggle_ExcludesWindow_AndTracksIt()
    {
        IntPtr hwnd = _form.Handle;

        Assert.False(_exclusion.IsExcluded(hwnd));
        Assert.False(_exclusion.WasExcludedByUs(hwnd));

        _exclusion.Toggle(hwnd);

        Assert.True(_exclusion.IsExcluded(hwnd), "Window should be excluded after toggle");
        Assert.True(_exclusion.WasExcludedByUs(hwnd), "Window should be tracked as excluded-by-us");
    }

    [Fact]
    public void Toggle_RestoresWindow_AndUntracksIt()
    {
        IntPtr hwnd = _form.Handle;

        // Exclude first.
        _exclusion.Toggle(hwnd);
        Assert.True(_exclusion.IsExcluded(hwnd));

        // Toggle back to visible.
        _exclusion.Toggle(hwnd);

        Assert.False(_exclusion.IsExcluded(hwnd), "Window should be restored after second toggle");
        Assert.False(_exclusion.WasExcludedByUs(hwnd), "Window should be untracked after restore");
    }

    [Fact]
    public void SetExcluded_True_ExcludesAndTracks()
    {
        IntPtr hwnd = _form.Handle;

        _exclusion.SetExcluded(hwnd, excluded: true);

        Assert.True(_exclusion.IsExcluded(hwnd));
        Assert.True(_exclusion.WasExcludedByUs(hwnd));
    }

    [Fact]
    public void SetExcluded_False_RestoresAndUntracks()
    {
        IntPtr hwnd = _form.Handle;

        _exclusion.SetExcluded(hwnd, excluded: true);
        _exclusion.SetExcluded(hwnd, excluded: false);

        Assert.False(_exclusion.IsExcluded(hwnd));
        Assert.False(_exclusion.WasExcludedByUs(hwnd));
    }

    [Fact]
    public void RestoreAll_RestoresExcludedWindows_AndClearsTracker()
    {
        IntPtr hwnd = _form.Handle;

        _exclusion.SetExcluded(hwnd, excluded: true);
        Assert.True(_exclusion.IsExcluded(hwnd));

        int count = _exclusion.RestoreAll();

        Assert.True(count >= 1, "RestoreAll should restore at least 1 window");
        Assert.False(_exclusion.IsExcluded(hwnd), "Window should be restored after RestoreAll");
        Assert.False(_exclusion.WasExcludedByUs(hwnd), "Tracker should be empty after RestoreAll");
    }

    [Fact]
    public void RestoreAll_WithNoExcludedWindows_ReturnsZero()
    {
        int count = _exclusion.RestoreAll();
        Assert.Equal(0, count);
    }

    [Fact]
    public void PruneStale_RemovesInvalidHandles()
    {
        // Add a fake (invalid) HWND to the tracker.
        IntPtr fakeHwnd = new(0x12345678);
        _tracker.Add(fakeHwnd);
        Assert.True(_tracker.Contains(fakeHwnd));

        _exclusion.PruneStale();

        Assert.False(_tracker.Contains(fakeHwnd), "Invalid HWND should be pruned");
    }

    [Fact]
    public void PruneStale_KeepsValidHandles()
    {
        IntPtr hwnd = _form.Handle;
        _tracker.Add(hwnd);

        _exclusion.PruneStale();

        Assert.True(_tracker.Contains(hwnd), "Valid HWND should survive pruning");
    }
}
