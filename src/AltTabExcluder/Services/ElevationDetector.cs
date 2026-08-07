using Windows.Win32;
using Windows.Win32.Foundation;

namespace AltTabExcluder.Services;

/// <summary>
/// Detects whether a target window's process (and/or the current process) is
/// running elevated, so the app can warn the user that UIPI will block style
/// changes on an elevated target unless AltTabExcluder is also elevated.
/// </summary>
public static class ElevationDetector
{
    // The elevation state of the current process never changes during its
    // lifetime, so we compute it once at class init (first access) and cache it.
    // This avoids 4 P/Invokes (OpenProcess + OpenProcessToken +
    // GetTokenInformation + CloseHandle × 2) on every WinEvent callback.
    private static readonly bool _currentProcessElevated =
        IsProcessElevated((uint)Environment.ProcessId);

    /// <summary>True when the current AltTabExcluder process is running elevated.</summary>
    public static bool IsCurrentProcessElevated() => _currentProcessElevated;

    /// <summary>
    /// True when the process owning <paramref name="hwnd"/> is running elevated.
    /// Returns <c>false</c> if the process can't be queried (e.g. already exited).
    /// </summary>
    public static bool IsWindowElevated(IntPtr hwnd)
    {
        uint pid;
        unsafe { PInvoke.GetWindowThreadProcessId((HWND)hwnd, &pid); }
        return pid != 0 && IsProcessElevated(pid);
    }

    /// <summary>True when the process with the given id is running elevated.</summary>
    public static bool IsProcessElevated(uint processId)
    {
        IntPtr hProcess = NativeElevationInterop.OpenProcess(
            NativeElevationInterop.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (hProcess == IntPtr.Zero)
            return false;

        try
        {
            if (!NativeElevationInterop.OpenProcessToken(hProcess,
                NativeElevationInterop.TOKEN_QUERY, out IntPtr hToken))
                return false;

            try
            {
                return IsTokenElevated(hToken);
            }
            finally
            {
                NativeElevationInterop.CloseHandle(hToken);
            }
        }
        finally
        {
            NativeElevationInterop.CloseHandle(hProcess);
        }
    }

    private static bool IsTokenElevated(IntPtr hToken)
    {
        var te = new NativeElevationInterop.TOKEN_ELEVATION();
        if (!NativeElevationInterop.GetTokenInformation(hToken,
            NativeElevationInterop.TokenElevation, ref te,
            (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeElevationInterop.TOKEN_ELEVATION>(),
            out _))
        {
            return false;
        }
        return te.TokenIsElevated != 0;
    }
}
