using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace AltTabExcluder.Services;

/// <summary>
/// Enumerates currently open, visible top-level windows via <c>EnumWindows</c>
/// and enriches each with process name and icon metadata.
/// </summary>
public static class WindowEnumerationService
{
    private const int MaxTitle = 512;

    // Class names we never want to show.
    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shell_TrayWnd",   // Taskbar
        "Progman",         // Desktop
        "WorkerW",         // Desktop wallpaper host
        "MSCTFIME UI",     // IME
        "IME",             // IME
    };

    public static IReadOnlyList<WindowInfo> GetOpenWindows()
    {
        var results = new List<WindowInfo>();
        int currentPid = Environment.ProcessId;

        unsafe
        {
            PInvoke.EnumWindows((hwnd, lParam) =>
            {
                try
                {
                    AddIfRelevant(results, hwnd, currentPid);
                }
                catch
                {
                    // Never let a single bad window abort enumeration.
                }
                return true;
            }, (LPARAM)0);
        }

        // Stable, user-friendly ordering: by process name then title.
        results.Sort((a, b) =>
        {
            int c = string.Compare(a.ProcessName, b.ProcessName, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.Compare(a.WindowTitle, b.WindowTitle, StringComparison.OrdinalIgnoreCase);
        });

        return results;
    }

    private static unsafe void AddIfRelevant(List<WindowInfo> results, HWND hwnd, int currentPid)
    {
        if (!PInvoke.IsWindowVisible(hwnd))
            return;

        // Skip the desktop / taskbar / IME helper windows.
        char* cls = stackalloc char[256];
        int clsLen = PInvoke.GetClassName(hwnd, (PWSTR)cls, 256);
        string className = clsLen > 0 ? new string(cls, 0, clsLen) : string.Empty;
        if (IgnoredClasses.Contains(className))
            return;

        int titleLen = PInvoke.GetWindowTextLength(hwnd);
        if (titleLen <= 0)
            return;

        char* titleBuf = stackalloc char[MaxTitle];
        int written = PInvoke.GetWindowText(hwnd, (PWSTR)titleBuf, MaxTitle);
        string title = written > 0 ? new string(titleBuf, 0, written) : string.Empty;
        if (string.IsNullOrWhiteSpace(title))
            return;

        uint pid;
        PInvoke.GetWindowThreadProcessId(hwnd, &pid);
        if (pid == currentPid)
            return; // Don't list our own windows.

        string procName = "<unknown>";
        string? imagePath = null;
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            procName = proc.ProcessName;
            try { imagePath = proc.MainModule?.FileName; }
            catch { /* elevated / inaccessible - leave path null */ }
        }
        catch
        {
            // Process may have exited between enumeration and lookup.
        }

        results.Add(new WindowInfo
        {
            Hwnd = (IntPtr)hwnd,
            ProcessId = pid,
            ProcessName = procName,
            WindowTitle = title,
            ProcessIcon = TryLoadIcon(imagePath),
            IsExcluded = WindowManager.IsWindowExcluded((IntPtr)hwnd),
            WasExcludedByUs = WindowManager.WasExcludedByUs((IntPtr)hwnd),
        });
    }

    /// <summary>
    /// Extracts a <see cref="System.Drawing.Icon"/> from the process's
    /// executable. The icon is returned undisposed; the caller owns it.
    /// </summary>
    private static Icon? TryLoadIcon(string? imagePath)
    {
        if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            return null;

        try
        {
            var icon = Icon.ExtractAssociatedIcon(imagePath);
            if (icon is null || icon.Handle == IntPtr.Zero)
                return null;
            return icon;
        }
        catch
        {
            return null;
        }
    }
}
