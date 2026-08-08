using System.Runtime.InteropServices;

namespace AltTabExcluder.Tests.Integration;

/// <summary>
/// Helper for integration tests that require a live desktop session.
/// Provides desktop-session detection and test-window lifecycle management.
/// </summary>
internal static class IntegrationTestHelper
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    /// <summary>
    /// Returns <c>true</c> if a desktop window is available (i.e. we're running
    /// in an interactive session, not headless). Integration tests call this to
    /// skip gracefully when no desktop is present.
    /// </summary>
    public static bool HasDesktopSession => GetDesktopWindow() != IntPtr.Zero;

    /// <summary>
    /// Creates and shows a WinForms <see cref="Form"/> with the given title,
    /// returning it for use in integration tests. The caller is responsible for
    /// disposing the form.
    /// </summary>
    public static Form CreateTestForm(string title, int width = 300, int height = 200)
    {
        var form = new Form
        {
            Text = title,
            Width = width,
            Height = height,
            StartPosition = FormStartPosition.Manual,
            Location = new System.Drawing.Point(100, 100),
            ShowInTaskbar = true,
        };
        form.Show();
        Application.DoEvents(); // Force the handle creation + initial paint.
        return form;
    }

    /// <summary>
    /// Pumps the message loop for a short duration (in milliseconds) so that
    /// async Win32 events (WinEventHook callbacks, timers) can fire.
    /// </summary>
    public static void PumpMessages(int milliseconds)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < milliseconds)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }
}
